import { IDBFactory } from 'fake-indexeddb'
import { beforeEach, expect, it, vi } from 'vitest'
import { JsonNumber, parseExactJson } from '../../shared/api/exactJson'
import { offlineAmounts, queueOfflineSale, synchronizeOutbox } from './workflow'
import { offlineStorage } from './storage'
import { envelope, id, item, lease } from './offline.test.support'
import { HttpClient } from '../../shared/api/http'
import { createMediPosService } from '../access'
import { workspace } from '../../test/operations'

beforeEach(() => vi.stubGlobal('indexedDB', new IDBFactory()))
it('uses exact conversion and ToEven line rounding while treating stock as advisory', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item()], true)
  const p = item(); p.product!.retailPrice = new JsonNumber('0.00025')
  const lines = [{ item: p, unitId: id(9), quantity: '1' }]
  expect(offlineAmounts(lines).total.literal).toBe('0.0002')
  const e = await queueOfflineSale(l, lines, 'cash', '0.0002', l.preparedAt)
  const body = parseExactJson(e.payload) as { totalAmountSnapshot: JsonNumber }
  expect(body.totalAmountSnapshot.literal).toBe('2e-4'); expect(e.payloadHash).toMatch(/^[a-f0-9]{64}$/)
  expect((await offlineStorage.outbox(l))[0]?.status).toBe('pending')
  await expect(queueOfflineSale(l, lines, 'cash', '0.0003')).rejects.toThrow(/coincidir/)
  await expect(queueOfflineSale(l, lines, 'cash', '0.0002', l.expiresAt)).rejects.toThrow(/venció/)
  p.product!.retailPrice = new JsonNumber('0')
  await expect(queueOfflineSale(l, lines, 'cash', '0')).rejects.toThrow(/positivo/)
})
it('rejects duplicate lines and inexact base conversions', () => {
  const line = { item: item(), unitId: id(9), quantity: '1' }
  expect(() => offlineAmounts([line, line])).toThrow(/dupliques/)
  line.item.product!.units[0]!.conversionToBase = new JsonNumber('0.000000000001')
  expect(() => offlineAmounts([{ ...line, quantity: '0.000000000001' }])).toThrow(/precisión/)
})
it('does not send pending writes after a real-session denial or under another user', async () => {
  const l = lease(); await offlineStorage.prepare(l, [], true); await offlineStorage.enqueue(l, envelope())
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => new Response('{"code":"auth.session_required"}', { status: 401, headers: { 'Content-Type': 'application/json' } }))
  const http = new HttpClient(fetcher); const service = createMediPosService(http)
  await expect(synchronizeOutbox(l, service, http, new AbortController().signal)).rejects.toBeDefined()
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  expect((await offlineStorage.outbox(l, true))[0]?.status).toBe('pending')
})
it('replays the original event and payload after a lost batch response', async () => {
  const l = lease(); const e = envelope(); await offlineStorage.prepare(l, [], true); await offlineStorage.enqueue(l, e)
  const sent: Array<{ events: unknown[]; batchRequestId: string }> = []; const time = new Date().toISOString()
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    if (String(path) === '/auth/csrf') return Response.json({ requestToken: 'unit-csrf' })
    expect(init?.method).toBe('POST'); sent.push(JSON.parse(String(init?.body)))
    if (sent.length === 1) throw new TypeError('Synthetic lost response')
    return Response.json({ protocolVersion: 1, deviceId: l.device.deviceId, serverReceivedAtUtc: time,
      results: [{ eventId: e.eventId, status: 'accepted', resultCode: 'sync.sale_accepted', receiptId: id(12), resultReferenceId: id(13), receivedAtUtc: time, processedAtUtc: time, isReplay: true, retryable: false }],
      pendingCount: 0, acceptedCount: 1, conflictCount: 0, rejectedCount: 0 })
  })
  const http = new HttpClient(fetcher); const service = createMediPosService(http)
  vi.spyOn(service, 'session').mockResolvedValue({ userId: l.userId })
  vi.spyOn(service, 'access').mockResolvedValue({ isAllowed: true, code: 'access.allowed', context: { ...workspace.access, userId: l.userId, tenantId: l.tenantId, branchId: l.branchId, membershipId: l.membershipId } })
  const signal = new AbortController().signal
  await expect(synchronizeOutbox(l, service, http, signal, true)).rejects.toMatchObject({ outcomeUnknown: true })
  expect((await offlineStorage.outbox(l))[0]).toMatchObject({ status: 'pending', envelope: e })
  await expect(synchronizeOutbox(l, service, http, signal, true)).resolves.toBe(true)
  expect(sent.map(batch => batch.events)).toEqual([[e], [e]])
  expect(sent[0]?.batchRequestId).not.toBe(sent[1]?.batchRequestId) // Transport tracing only.
  expect((await offlineStorage.outbox(l))[0]).toMatchObject({ status: 'accepted', envelope: e })
})
