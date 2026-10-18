import { expect, it, vi } from 'vitest'
import { waitFor } from '@testing-library/react'
import { HttpClient } from './http'

const response = (body: unknown = {}) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
it('serializes platform requests within its stricter budget without blocking tenant reads', async () => {
  const finishes: (() => void)[] = []
  const fetcher = vi.fn<typeof fetch>().mockImplementation(() => new Promise(resolve => finishes.push(() => resolve(response()))))
  const http = new HttpClient(fetcher)
  const first = http.request('/platform/session'); const second = http.request('/platform/tenants'); const tenant = http.request('/api/me/tenants')
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2))
  expect(fetcher.mock.calls.map(([path]) => path)).toEqual(['/platform/session', '/api/me/tenants'])
  finishes.shift()!(); await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3))
  finishes.splice(0).forEach(finish => finish()); await Promise.all([first, second, tenant])
})
it('rejects malformed or misplaced idempotency headers before CSRF and any network request', async () => {
  const fetcher = vi.fn<typeof fetch>(); const http = new HttpClient(fetcher)
  await expect(http.request('/platform/tenants', { method: 'POST', body: {}, idempotencyKey: 'bad' })).rejects.toMatchObject({ code: 'client.invalid_request' })
  await expect(http.request('/api/operation', { method: 'POST', body: {}, idempotencyKey: '019a0162-0000-7000-8000-000000000001' })).rejects.toMatchObject({ code: 'client.invalid_request' })
  expect(fetcher).not.toHaveBeenCalled()
})
it('does not dispatch a request whose signal was already aborted', async () => {
  const fetcher = vi.fn<typeof fetch>(); const controller = new AbortController(); controller.abort()
  await expect(new HttpClient(fetcher).request('/api/tenants', { signal: controller.signal })).rejects.toBe(controller.signal.reason)
  expect(fetcher).not.toHaveBeenCalled()
})
it('queues a five-query focus burst within server concurrency limits, without dropping or retrying requests', async () => {
  const finishes: (() => void)[] = []
  const fetcher = vi.fn<typeof fetch>().mockImplementation(() => new Promise(resolve => { finishes.push(() => resolve(response())) }))
  const http = new HttpClient(fetcher)
  const results = ['tenants', 'branches', 'access', 'cash-sessions/current', 'products/search'].map(path => http.request('/api/' + path))
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2))
  finishes.shift()!()
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3))
  finishes.shift()!()
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(4))
  finishes.shift()!()
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(5))
  finishes.splice(0).forEach(finish => finish())
  await Promise.all(results)
  expect(fetcher.mock.calls.map(([path]) => path)).toEqual(['/api/tenants', '/api/branches', '/api/access', '/api/cash-sessions/current', '/api/products/search'])
})
it('cancels a queued search before network dispatch and releases the remaining slots', async () => {
  const finishes: (() => void)[] = []
  const fetcher = vi.fn<typeof fetch>().mockImplementation(() => new Promise(resolve => { finishes.push(() => resolve(response())) }))
  const http = new HttpClient(fetcher); const controller = new AbortController()
  const first = http.request('/api/one'); const second = http.request('/api/two')
  const canceled = http.request('/api/old-search', { signal: controller.signal })
  const rejected = expect(canceled).rejects.toMatchObject({ name: 'AbortError' })
  controller.abort(); await rejected
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2))
  finishes.splice(0).forEach(finish => finish()); await Promise.all([first, second])
  expect(fetcher.mock.calls.some(([path]) => path === '/api/old-search')).toBe(false)
})
it('acquires fresh CSRF for each mutation without deadlocking the same client or retrying 429', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => path === '/auth/csrf' ? response({ requestToken: 'unit-csrf' }) :
    new Response(JSON.stringify({ code: 'api.rate_limited' }), { status: 429, headers: { 'Content-Type': 'application/problem+json' } }))
  const http = new HttpClient(fetcher)
  const results = await Promise.allSettled(Array.from({ length: 5 }, (_, index) => http.request('/api/operation-' + index, { method: 'POST', body: {} })))
  expect(results.every(result => result.status === 'rejected' && result.reason.status === 429 && !result.reason.outcomeUnknown)).toBe(true)
  expect(fetcher.mock.calls.filter(([path]) => path === '/auth/csrf')).toHaveLength(5)
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(5)
})
