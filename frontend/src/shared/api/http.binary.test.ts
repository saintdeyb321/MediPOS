import { expect, it, vi } from 'vitest'
import { HttpClient } from './http'

it('downloads PDF through the same CSRF/cookie pipeline and respects its binary content type', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? new Response('{"requestToken":"binary-unit-csrf"}', { headers: { 'Content-Type': 'application/json' } }) : new Response('%PDF-1.7\nUNIT', { headers: { 'Content-Type': 'application/pdf' } }))
  const result = await new HttpClient(fetcher).request('/api/report', { method: 'POST', body: {}, responseType: 'blob', timeoutMs: 35000 })
  expect(result).toHaveProperty('type', 'application/pdf'); expect(fetcher).toHaveBeenCalledTimes(2)
  const request = fetcher.mock.calls[1]![1]!; expect(request.credentials).toBe('same-origin'); expect(new Headers(request.headers).get('X-CSRF-TOKEN')).toBe('binary-unit-csrf')
})
it.each([401, 403, 409, 429, 503])('preserves Problem Details HTTP %i for a failed binary export without retry', async status => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response('{"code":"reports.unit_error","traceId":"unit-trace"}', { status, headers: { 'Content-Type': 'application/problem+json' } }))
  await expect(new HttpClient(fetcher).request('/api/report', { responseType: 'blob' })).rejects.toMatchObject({ status, code: 'reports.unit_error', traceId: 'unit-trace' }); expect(fetcher).toHaveBeenCalledTimes(1)
})
it('rejects HTML as a report and refuses unbounded or invalid timeouts before sending', async () => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response('<html>bad</html>', { headers: { 'Content-Type': 'text/html' } })); const http = new HttpClient(fetcher)
  await expect(http.request('/api/report', { responseType: 'blob' })).rejects.toMatchObject({ code: 'client.unexpected_response' })
  for (const timeoutMs of [0, -1, 60001, 1.5]) await expect(http.request('/api/report', { responseType: 'blob', timeoutMs })).rejects.toMatchObject({ code: 'client.invalid_request' })
  expect(fetcher).toHaveBeenCalledTimes(1)
})
