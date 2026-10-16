import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { id, json, lot, n, operationServer, time, workspace } from '../../test/operations'
import { inventoryApi } from './api'

it('reads all three actual inventory routes with bounded pagination and product filters', async () => {
  const fake = operationServer(); const api = inventoryApi(new HttpClient(fake.fetcher))
  await api.stock(workspace, id(10), 20, 20); await api.lots(workspace, undefined); await api.expiring(workspace, id(10), 40, 20)
  expect(fake.fetcher.mock.calls.map(([path]) => String(path))).toEqual([
    `/api/tenants/${id(2)}/branches/${id(5)}/inventory/stock?offset=20&limit=20&productId=${id(10)}`,
    `/api/tenants/${id(2)}/branches/${id(5)}/inventory/lots?offset=0&limit=20`,
    `/api/tenants/${id(2)}/branches/${id(5)}/inventory/expiring-lots?offset=40&limit=20&productId=${id(10)}`,
  ])
  await expect(api.lots(workspace, undefined, 0, 101)).rejects.toThrow('Invalid paging')
})
it('sends signed exact adjustment and Owner minimum with CSRF and verifies returned scope', async () => {
  const fake = operationServer(); const api = inventoryApi(new HttpClient(fake.fetcher))
  expect((await api.adjust(workspace, lot.inventoryLotId, n('-0.000000000001'), 'Motivo unitario')).after.literal).toBe('19.999999999999')
  expect((await api.minimum(workspace, id(10), n('1.000000000001'))).minimumStockBase.literal).toBe('1.000000000001')
  expect(fake.writes()[0]?.[1]?.body).toBe('{"quantityDeltaBase":-0.000000000001,"reason":"Motivo unitario"}')
  expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
})
it('rejects cross-branch expiration rows and invalid calendar dates', async () => {
  const record = { inventoryLotId: id(30), branchId: id(99), businessProductId: id(10), productName: 'Unit', quantityAvailableBase: n('1'), expirationDate: '2027-01-01', costValue: null, potentialSaleValue: n('1') }
  for (const row of [record, { ...record, branchId: id(5), expirationDate: '2027-02-30' }]) {
    const api = inventoryApi(new HttpClient(vi.fn<typeof fetch>().mockResolvedValue(json([row]))))
    await expect(api.expiring(workspace, undefined)).rejects.toThrow('client.unexpected_response')
  }
})
it('never accepts a successful adjustment for a different lot or minimum for another tenant', async () => {
  for (const record of [{ inventoryLotId: id(99), stockMovementId: id(31), before: n('1'), after: n('2') }, { id: id(32), tenantId: id(99), branchId: id(5), businessProductId: id(10), minimumStockBase: n('1'), updatedAt: time, updatedByActorId: id(1) }]) {
    const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json(record))
    const api = inventoryApi(new HttpClient(fetcher))
    await expect('inventoryLotId' in record ? api.adjust(workspace, id(30), n('1'), 'Motivo') : api.minimum(workspace, id(10), n('1'))).rejects.toMatchObject({ outcomeUnknown: true })
  }
})
