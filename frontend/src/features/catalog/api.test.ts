import { expect, it, vi } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, stringifyExactJson } from '../../shared/api/exactJson'
import { catalogApi } from './api'

const id = (value: string) => `019a0162-0000-7000-8000-${value.padStart(12, '0')}`
const scope = { tenantId: id('2'), branchId: id('5') }
const record = { id: id('10'), tenantId: scope.tenantId, globalProductId: null, internalCode: 'UNIT', productType: 'retail', name: 'Producto unitario', categoryId: id('8'),
  brandOrLaboratory: 'Marca de prueba', barcode: null, medicine: null, retailPrice: new JsonNumber('1.2345'), wholesalePrice: null, isActive: true, createdAt: '2026-10-16T12:00:00Z' }
const json = (data: unknown) => new Response(stringifyExactJson(data), { headers: { 'Content-Type': 'application/json' } })
it('uses real route contracts, bounded pagination and exact decimal decoding', async () => {
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json([record]))
  const result = await catalogApi(new HttpClient(fetcher)).list(scope, 50, 50)
  expect(String(fetcher.mock.calls[0]?.[0])).toBe(`/api/tenants/${scope.tenantId}/products?offset=50&limit=50`)
  expect(result[0]?.retailPrice.literal).toBe('1.2345')
  await expect(catalogApi(new HttpClient(fetcher)).list(scope, 10001)).rejects.toThrow('Invalid catalog page')
  expect(fetcher).toHaveBeenCalledTimes(1)
})
it('rejects responses from another tenant or another product', async () => {
  for (const product of [{ ...record, tenantId: id('99') }, { ...record, id: id('99') }]) {
    const api = catalogApi(new HttpClient(vi.fn<typeof fetch>().mockResolvedValue(json({ product, units: [] }))))
    await expect(api.detail(scope, record.id)).rejects.toThrow('client.unexpected_response')
  }
})
it('sends reviewed prices with CSRF and exact literals without mutation retries', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json(record))
  await catalogApi(new HttpClient(fetcher)).prices(scope, record.id, { retailPrice: new JsonNumber('1.2345'), wholesalePrice: null })
  expect(fetcher.mock.calls[1]?.[1]?.body).toBe('{"retailPrice":1.2345,"wholesalePrice":null}')
  expect(new Headers(fetcher.mock.calls[1]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
  expect(fetcher.mock.calls[1]?.[1]?.method).toBe('PUT')
})
it('blocks unit replacement if fresh detail has any existing presentation, without any write', async () => {
  const unit = { id: id('11'), name: 'Unidad', conversionToBase: new JsonNumber('1'), isBaseUnit: true, isActive: true }
  const fetcher = vi.fn<typeof fetch>().mockResolvedValue(json({ product: record, units: [unit] }))
  await expect(catalogApi(new HttpClient(fetcher)).initializeUnits(scope, record.id, [{ name: 'Nueva', conversionToBase: new JsonNumber('1'), isBaseUnit: true, isActive: true }])).rejects.toThrow('catalog.units_replacement_blocked')
  expect(fetcher).toHaveBeenCalledTimes(1)
  expect(fetcher.mock.calls[0]?.[1]?.method).toBe('GET')
})
it('does not mistake an empty read for an atomic right to initialize units', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    if (String(path) === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if (init?.method === 'GET') return json({ product: record, units: [] })
    throw new TypeError('Unit-test response loss')
  })
  await expect(catalogApi(new HttpClient(fetcher)).initializeUnits(scope, record.id, [{ name: 'Unidad', conversionToBase: new JsonNumber('1'), isBaseUnit: true, isActive: true }])).rejects.toThrow('catalog.units_atomic_contract_required')
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'PUT' || init?.method === 'POST')).toHaveLength(0)
})
it('treats an invalid successful mutation body as uncertain, never a successful product', async () => {
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async path => String(path) === '/auth/csrf' ? json({ requestToken: 'unit-csrf' }) : json({}))
  await expect(catalogApi(new HttpClient(fetcher)).status(scope, record.id, false)).rejects.toMatchObject({ outcomeUnknown: true })
})
