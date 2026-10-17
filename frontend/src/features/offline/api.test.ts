import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { stringifyExactJson } from '../../shared/api/exactJson'
import { offlineApi } from './api'
import { envelope, id, item, lease } from './offline.test.support'

function setup(data: unknown, status = 200) {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => new Response(stringifyExactJson(String(path) === '/auth/csrf' ? { requestToken: 'unit-csrf' } : data), { status: String(path) === '/auth/csrf' ? 200 : status, headers: { 'Content-Type': 'application/json' } }))
  return { api: offlineApi(new HttpClient(fetcher)), fetcher }
}
it('reads a bounded complete snapshot with exact stock/version and the bound branch/device/cursor', async () => {
  const l = lease(); const s = setup({ schemaVersion: 1, branchId: l.branchId, serverGeneratedAtUtc: new Date().toISOString(), items: [item()], nextCursor: 'unit-next', hasMore: false, isFullSnapshot: true, requiresFullResync: false, stockAsOfUtc: new Date().toISOString(), resultCode: null, stockIsAdvisory: true })
  const page = await s.api.catalog(l, l.device.deviceId, 'snapshot', null)
  expect(page.items[0]?.version.literal).toBe('9007199254740993'); expect(page.items[0]?.product?.retailPrice.literal).toBe('0.0001')
  expect(s.fetcher.mock.calls[0]?.[0]).toContain('/catalog/snapshot?branchId=' + l.branchId + '&pageSize=100')
})
it('uses immutable event envelopes and CSRF; pending is reception only and accepted needs a real reference', async () => {
  const l = lease(); const e = envelope(); const receipt = { eventId: e.eventId, status: 'pending', resultCode: 'sync.pending', receiptId: id(20), resultReferenceId: null, receivedAtUtc: new Date().toISOString(), processedAtUtc: null, isReplay: false, retryable: true }
  const body = { protocolVersion: 1, deviceId: l.device.deviceId, serverReceivedAtUtc: new Date().toISOString(), results: [receipt], pendingCount: 1, acceptedCount: 0, conflictCount: 0, rejectedCount: 0 }
  const s = setup(body); const result = await s.api.batch(l, l.device.deviceId, [e])
  expect(result.results[0]?.status).toBe('pending'); const write = s.fetcher.mock.calls.find(([, init]) => init?.method === 'POST')!
  expect(new Headers(write[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf'); expect(String(write[1]?.body)).toContain(e.payloadHash)
  const bad = setup({ ...body, results: [{ ...receipt, status: 'accepted', retryable: false }] })
  await expect(bad.api.batch(l, l.device.deviceId, [e])).rejects.toMatchObject({ outcomeUnknown: true })
})
it.each([401, 403, 409, 429, 503])('never retries a batch automatically inside transport on HTTP %s', async status => {
  const l = lease(); const s = setup({ code: 'unit.denied' }, status)
  await expect(s.api.batch(l, l.device.deviceId, [envelope()])).rejects.toMatchObject({ status })
  expect(s.fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
})
