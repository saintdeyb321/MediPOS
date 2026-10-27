import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { id, json, n, operationServer, purchase, storedLine, receiptRevision, workspace } from '../../test/operations'
import { purchasingApi } from './api'

it('uses supplier pagination, official branch reference search and scoped receipt GET, never a general directory', async () => {
  const fake = operationServer(); fake.state.purchases = [purchase]
  const api = purchasingApi(new HttpClient(fake.fetcher))
  await api.suppliers(workspace, 20, 20); await api.search(workspace, 'REF-UNIT', 40, 20); const result = await api.read(workspace, purchase.id)
  expect(result.purchase.id).toBe(purchase.id)
  expect(fake.fetcher.mock.calls.map(([path]) => String(path))).toEqual([
    `/api/tenants/${id(2)}/branches/${id(5)}/suppliers?offset=20&limit=20`,
    `/api/tenants/${id(2)}/branches/${id(5)}/purchases/search?offset=40&limit=20&reference=REF-UNIT`,
    `/api/tenants/${id(2)}/branches/${id(5)}/purchases/${id(20)}`,
  ])
  await expect(api.suppliers(workspace, -1)).rejects.toThrow('Invalid page')
})
it('sends only official DTOs with exact numbers and CSRF, with the reviewed revision and no identity headers', async () => {
  const fake = operationServer(); const api = purchasingApi(new HttpClient(fake.fetcher))
  await api.createSupplier(workspace, { name: 'Proveedor unitario', ruc: null, contact: null })
  await api.create(workspace, null, 'REF-UNIT')
  await api.lines(workspace, purchase.id, [{ businessProductId: id(10), productUnitId: id(11), quantity: n('9007199254740993'), unitCost: n('0.1234567890123456789012345678'), batchNumber: 'Lote', expirationDate: '2027-12-31' }], receiptRevision(fake.state.lines))
  await api.confirm(workspace, purchase.id, receiptRevision(fake.state.lines))
  const writes = fake.writes(); expect(writes).toHaveLength(4)
  expect(String(writes[2]?.[0])).toBe(`/api/tenants/${id(2)}/purchases/${id(20)}/lines`)
  expect(writes[2]?.[1]?.body).toContain('"quantity":9007199254740993,"unitCost":0.1234567890123456789012345678')
  expect(writes[3]?.[1]?.body).toBe(JSON.stringify({ expectedRevision: receiptRevision(fake.state.lines) }))
  for (const [, init] of writes) { expect(new Headers(init?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf'); expect(init?.credentials).toBe('same-origin'); expect(String(init?.body)).not.toMatch(/expectedVersion|actorId|tenantId|idempotency/i) }
})
it('rejects wrong branch/id on reads and marks malformed successful writes uncertain', async () => {
  for (const data of [{ purchase: { ...purchase, branchId: id(99) }, lines: [] }, { purchase: { ...purchase, id: id(99) }, lines: [storedLine] }]) {
    const api = purchasingApi(new HttpClient(vi.fn<typeof fetch>().mockResolvedValue(json(data))))
    await expect(api.read(workspace, purchase.id)).rejects.toThrow('client.unexpected_response')
  }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json({}))
  await expect(purchasingApi(new HttpClient(fetcher)).create(workspace, null, null)).rejects.toMatchObject({ outcomeUnknown: true })
})
it.each([403, 409, 429, 503])('preserves HTTP %s and never retries a mutation', async status => {
  const fake = operationServer(); fake.state.reject = status
  await expect(purchasingApi(new HttpClient(fake.fetcher)).create(workspace, null, null)).rejects.toMatchObject({ status, outcomeUnknown: status >= 500 })
  expect(fake.writes()).toHaveLength(1)
})
