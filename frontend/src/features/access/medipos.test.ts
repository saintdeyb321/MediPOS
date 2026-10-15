import { afterEach, describe, expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { errorMessage } from './errors'
import { createMediPosService, parseAccess, parseBranches, parseSession, parseTenants } from './medipos'

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
describe('same-origin HTTP and native OIDC contract', () => {
  it('sends cookies with GET, no browser-controlled headers or CSRF request', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ items: [] }))
    await new HttpClient(fetcher).request('/api/me/tenants')
    expect(fetcher).toHaveBeenCalledOnce()
    const [path, options] = fetcher.mock.calls[0]!
    expect(path).toBe('/api/me/tenants'); expect(options).toMatchObject({ credentials: 'same-origin', redirect: 'error', cache: 'no-store' })
    expect(new Headers(options?.headers).has('Origin')).toBe(false)
    expect(new Headers(options?.headers).has('Sec-Fetch-Site')).toBe(false)
    expect(new Headers(options?.headers).has('X-CSRF-TOKEN')).toBe(false)
  })
  it('obtains fresh CSRF before each mutation and never persists it', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem')
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => path === '/auth/csrf' ? json({ requestToken: 'transient-csrf' }) : new Response(null, { status: 204 }))
    const http = new HttpClient(fetcher)
    await http.request('/auth/logout', { method: 'POST' }); await http.request('/auth/logout', { method: 'POST' })
    expect(fetcher.mock.calls.map(call => call[0])).toEqual(['/auth/csrf', '/auth/logout', '/auth/csrf', '/auth/logout'])
    expect(new Headers(fetcher.mock.calls[1]![1]?.headers).get('X-CSRF-TOKEN')).toBe('transient-csrf')
    expect(storage).not.toHaveBeenCalled()
  })
  it('does not send a mutation if CSRF acquisition fails', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'auth.csrf_invalid' }, 400))
    await expect(new HttpClient(fetcher).request('/auth/logout', { method: 'POST' })).rejects.toMatchObject({ status: 400, code: 'auth.csrf_invalid' })
    expect(fetcher).toHaveBeenCalledOnce()
  })
  it('preserves a stable Problem code but never displays server titles, detail or payload', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'access.license_denied', traceId: 'abc123', title: 'PRIVATE', detail: 'connection-string-secret' }, 403))
    const result = await new HttpClient(fetcher).request('/api/me/tenants').catch(error => error)
    expect(result).toMatchObject({ status: 403, code: 'access.license_denied', traceId: 'abc123' })
    expect(errorMessage(result)).toContain('licencia'); expect(errorMessage(result)).not.toContain('PRIVATE'); expect(errorMessage(result)).not.toContain('secret')
  })
  it('rejects an HTML SPA fallback rather than treating it as a successful session', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response('<html>not API</html>', { headers: { 'Content-Type': 'text/html' } }))
    await expect(new HttpClient(fetcher).request('/auth/me')).rejects.toMatchObject({ code: 'client.unexpected_response' })
  })
  it.each(['https://other.example/api/me', '//other.example/auth/me', '/api/\\other.example', '/unrelated'])('rejects an unsafe API path %s', async path => {
    const fetcher = vi.fn<typeof fetch>()
    await expect(new HttpClient(fetcher).request(path)).rejects.toMatchObject({ code: 'client.invalid_path' }); expect(fetcher).not.toHaveBeenCalled()
  })
  it('requires HTTPS without enabling an HTTP development bypass', async () => {
    vi.stubGlobal('window', { location: { protocol: 'http:' } })
    const fetcher = vi.fn<typeof fetch>()
    await expect(new HttpClient(fetcher).request('/auth/me')).rejects.toMatchObject({ code: 'client.https_required' }); expect(fetcher).not.toHaveBeenCalled()
  })
  it('passes cancellation through and does not retry an aborted request', async () => {
    const controller = new AbortController(); controller.abort()
    const error = new DOMException('Aborted', 'AbortError')
    const fetcher = vi.fn<typeof fetch>().mockRejectedValue(error)
    await expect(new HttpClient(fetcher).request('/api/me/tenants', { signal: controller.signal })).rejects.toBe(error)
    expect(fetcher).toHaveBeenCalledOnce(); expect(fetcher.mock.calls[0]![1]?.signal).toBe(controller.signal)
  })
  it('notifies subscribers of 401 and never retries a failed mutation', async () => {
    const listener = vi.fn(); const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(json({ requestToken: 'csrf' })).mockResolvedValue(json({ code: 'auth.session_required' }, 401))
    const http = new HttpClient(fetcher); const unsubscribe = http.subscribeUnauthorized(listener)
    await expect(http.request('/auth/logout', { method: 'POST' })).rejects.toMatchObject({ status: 401 })
    expect(listener).toHaveBeenCalledOnce(); expect(fetcher).toHaveBeenCalledTimes(2); unsubscribe()
  })
  it('preserves cancellation while consuming a response body', async () => {
    const controller = new AbortController(); const error = new DOMException('Aborted', 'AbortError')
    const response = json({})
    vi.spyOn(response, 'json').mockImplementation(async () => { controller.abort(); throw error })
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(response)
    await expect(new HttpClient(fetcher).request('/api/me/tenants', { signal: controller.signal })).rejects.toBe(error)
    expect(fetcher).toHaveBeenCalledOnce()
  })
  it('uses a native POST form for the Google challenge with the existing antiforgery field', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ requestToken: 'csrf-before-navigation' }))
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(function (this: HTMLFormElement) {
      expect(this.method).toBe('post'); expect(this.getAttribute('action')).toBe('/auth/login')
      const input = this.querySelector('input'); expect(input?.name).toBe('__RequestVerificationToken'); expect(input?.value).toBe('csrf-before-navigation')
      expect(document.body.contains(this)).toBe(true)
    })
    await new HttpClient(fetcher).login()
    expect(submit).toHaveBeenCalledOnce(); expect(fetcher).toHaveBeenCalledOnce(); expect(document.querySelector('form')).toBeNull()
  })
  it('treats an unauthenticated /auth/me as no session, not a fabricated identity', async () => {
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ code: 'auth.session_required' }, 401))
    expect(await createMediPosService(new HttpClient(fetcher)).session()).toBeNull()
  })
})
describe('runtime contract validation', () => {
  const id = '019a0162-0000-7000-8000-000000000001'
  it('rejects malformed user IDs and numeric/unknown membership roles', () => {
    expect(() => parseSession({ userId: 'invalid' })).toThrow()
    expect(() => parseTenants({ items: [{ tenantId: id, tradingName: 'Botica', role: 0 }], nextAfterTenantId: null })).toThrow()
    expect(() => parseTenants({ items: [{ tenantId: id, tradingName: 'Botica', role: 'superadmin' }], nextAfterTenantId: null })).toThrow()
  })
  it('rejects branch data from another tenant and allowed access without a context', () => {
    expect(() => parseBranches([{ tenantId: 'other' }], id)).toThrow()
    expect(() => parseAccess({ isAllowed: true, code: 'access.allowed', context: null }, id, id)).toThrow()
    expect(parseAccess({ isAllowed: false, code: 'access.membership_missing', context: null }, id, id).isAllowed).toBe(false)
  })
})
