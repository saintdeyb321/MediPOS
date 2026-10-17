import { IDBFactory } from 'fake-indexeddb'
import { beforeEach, expect, it, vi } from 'vitest'
import { leaseValid, offlineStorage, scopeKey } from './storage'
import { envelope, id, item, lease, n } from './offline.test.support'

beforeEach(() => vi.stubGlobal('indexedDB', new IDBFactory()))
it('releases only the definitely rejected registration intention, keeping ambiguous or different intentions reserved', async () => {
  const l = lease(); await offlineStorage.reserveRegistration(l, 'original')
  await expect(offlineStorage.reserveRegistration(l, 'another')).rejects.toThrow()
  await offlineStorage.releaseUnsentRegistration(l, 'another'); expect(await offlineStorage.registration(l)).toBe('original')
  await offlineStorage.releaseUnsentRegistration(l, 'original'); expect(await offlineStorage.registration(l)).toBeNull()
  await offlineStorage.markRevocation(l, l.device.deviceId); await offlineStorage.lock()
  expect(await offlineStorage.revocation(l)).toBe(l.device.deviceId)
  await offlineStorage.markRevocation(l, null); expect(await offlineStorage.revocation(l)).toBeNull()
})
it('round trips exact decimals, cursor and large versions across a fresh database connection', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item()], true)
  expect((await offlineStorage.lease())?.cursor).toBe(l.cursor)
  const product = (await offlineStorage.catalog(l))[0]!
  expect(product.version.literal).toBe('9007199254740993')
  expect(product.stock?.physicalStockBase.literal).toBe('9007199254740993.000000000001')
  expect(product.product?.retailPrice.literal).toBe('0.0001')
})
it('applies only newer versions and retains tombstones against late upserts', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item('2')], true)
  await offlineStorage.prepare(l, [{ ...item('3'), operation: 'tombstone', product: null, stock: null }], false)
  await offlineStorage.prepare(l, [item('2')], false); expect(await offlineStorage.catalog(l)).toEqual([])
  await offlineStorage.prepare(l, [item('4')], false); expect(await offlineStorage.catalog(l)).toHaveLength(1)
})
it('commits cursor/catalog atomically and preserves the old snapshot after invalid input', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item('2')], true)
  await expect(offlineStorage.prepare({ ...l, cursor: 'bad-new' }, [item('3'), { ...item('4'), version: n('1e1000') }], false)).rejects.toThrow()
  expect((await offlineStorage.lease())?.cursor).toBe(l.cursor); expect((await offlineStorage.catalog(l))[0]?.version.literal).toBe('2')
})
it('durably adds an immutable UUID exactly once and rejects expired, replaced or foreign devices', async () => {
  const l = lease(); await offlineStorage.prepare(l, [], true); const e = envelope()
  await offlineStorage.enqueue(l, e); await expect(offlineStorage.enqueue(l, { ...e, payload: '{"changed":true}' })).rejects.toBeDefined()
  expect((await offlineStorage.outbox(l))[0]?.envelope).toEqual(e)
  await expect(offlineStorage.enqueue(l, envelope(), l.expiresAt)).rejects.toThrow()
  await expect(offlineStorage.enqueue({ ...l, nonce: 'other' }, envelope())).rejects.toThrow()
  await expect(offlineStorage.enqueue(l, { ...envelope(), deviceId: id(99) })).rejects.toThrow()
})
it('never treats a pending receipt as confirmed and preserves terminal idempotency', async () => {
  const l = lease(); await offlineStorage.prepare(l, [], true); const e = envelope(); await offlineStorage.enqueue(l, e)
  const pending = { eventId: e.eventId, status: 'pending' as const, resultCode: 'sync.pending', receiptId: id(12), resultReferenceId: null, receivedAtUtc: new Date().toISOString(), processedAtUtc: null, isReplay: false, retryable: true }
  await offlineStorage.acknowledge(l, [pending]); expect((await offlineStorage.outbox(l))[0]?.status).toBe('pending')
  const accepted = { ...pending, status: 'accepted' as const, resultCode: 'sync.sale_accepted', resultReferenceId: id(13), processedAtUtc: new Date().toISOString(), retryable: false }
  await offlineStorage.acknowledge(l, [accepted]); await offlineStorage.acknowledge(l, [{ ...accepted, isReplay: true }])
  expect((await offlineStorage.outbox(l))[0]?.status).toBe('accepted')
  await expect(offlineStorage.acknowledge(l, [{ ...accepted, status: 'rejected' }])).rejects.toThrow()
})
it('locks catalog and quarantines pending writes on logout/context change without cross-user access', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item()], true); await offlineStorage.enqueue(l, envelope())
  await offlineStorage.lock(); expect(await offlineStorage.lease()).toBeNull(); await expect(offlineStorage.catalog(l)).rejects.toThrow()
  await expect(offlineStorage.outbox(l)).rejects.toThrow()
  const other = { ...lease(), userId: id(20), branchId: id(21) }; await offlineStorage.prepare(other, [], true)
  expect(await offlineStorage.outbox(other)).toEqual([]); expect(scopeKey(other)).not.toBe(scopeKey(l))
  await expect(offlineStorage.outbox(l)).rejects.toThrow()
  expect(await offlineStorage.outbox(l, true)).toHaveLength(1) // explicit fresh-identity caller, not an offline authorization token
})
it('bounds offline duration and rejects clock rollback without extending the session', () => {
  const l = lease(); expect(leaseValid(l, l.preparedAt + 500)).toBe(true)
  expect(leaseValid(l, l.expiresAt)).toBe(false); expect(leaseValid(l, l.preparedAt - 2000)).toBe(false)
  expect(leaseValid({ ...l, expiresAt: l.preparedAt + 31 * 60_000 }, l.preparedAt)).toBe(false)
})
it('blocks cache use even when IndexedDB cleanup fails and only fresh online preparation can unlock it', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item()], true)
  const factory = indexedDB; vi.stubGlobal('indexedDB', { open() { throw new Error('Synthetic storage denial') } })
  await expect(offlineStorage.lock()).rejects.toThrow()
  expect(await offlineStorage.lease()).toBeNull()
  vi.stubGlobal('indexedDB', factory); await expect(offlineStorage.catalog(l)).rejects.toThrow()
  await offlineStorage.prepare({ ...l, nonce: crypto.randomUUID() }, [item()], true)
  expect(await offlineStorage.lease()).not.toBeNull()
})
