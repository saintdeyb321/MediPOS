import { createHash } from 'node:crypto'
import { render } from '@testing-library/react'
import { vi } from 'vitest'
import type { ReactNode } from 'react'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../app/providers/runtime'
import { WorkspaceContext } from '../features/access'
import type { Workspace } from '../features/access'
import { createPreviewService } from '../development/fixtures'
import { HttpClient } from '../shared/api/http'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../shared/api/exactJson'
import { add, multiply } from '../shared/api/decimal'
import type { ProductDetail } from '../features/catalog'
import type { Purchase, PurchaseLine, PurchaseLineInput, Supplier, SupplierInput } from '../features/purchasing/api'
import type { LotStock, ExpiringLot } from '../features/inventory/api'

// Synthetic transport/component data only: never a claim of PostgreSQL, OIDC or commercial integration.
export const id = (value: number) => `019a0162-0000-7000-8000-${String(value).padStart(12, '0')}`
export const n = (value: string) => new JsonNumber(value)
export const time = '2026-10-16T12:00:00Z'
export const workspace: Workspace = { tenantId: id(2), branchId: id(5), tenantName: 'Negocio unitario', base: `/boticas/${id(2)}/sucursales/${id(5)}`,
  session: { userId: id(1) }, branch: { id: id(5), tenantId: id(2), legalEntityId: id(4), name: 'Sede unitaria', isMainHub: false, createdAt: time },
  access: { userId: id(1), tenantId: id(2), branchId: id(5), membershipId: id(3), role: 'owner', membershipIsActive: true, branchAssigned: true, withinSchedule: true,
    licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: time } }
export const detail: ProductDetail = { revision: 'A'.repeat(64), product: { id: id(10), tenantId: id(2), globalProductId: null, internalCode: 'UNIT', productType: 'medicine', name: 'Producto sintético unitario', categoryId: id(8),
  brandOrLaboratory: 'Marca de prueba', barcode: null, medicine: { components: [{ ingredient: 'Ingrediente sintético', strengthNormalized: '1 mg' }], activeIngredients: ['Ingrediente sintético'], normalizedStrength: '1 mg', dosageForm: 'Tableta', route: 'Oral', sanitaryRegistration: null },
  retailPrice: n('1.0000'), wholesalePrice: null, isActive: true, createdAt: time },
  units: [{ id: id(11), name: 'Unidad', conversionToBase: n('1'), isBaseUnit: true, isActive: true }, { id: id(12), name: 'Caja de 10', conversionToBase: n('10'), isBaseUnit: false, isActive: true }] }
export const purchase: Purchase = { id: id(20), branchId: id(5), supplierId: null, documentReference: 'REF-UNIT', status: 'draft', createdAt: time, confirmedAt: null, createdByActorId: id(1), confirmedByActorId: null }
export const storedLine: PurchaseLine = { id: id(21), businessProductId: id(10), quantity: n('2'), unitNameSnapshot: 'Caja de 10', conversionToBaseSnapshot: n('10'), baseQuantity: n('20'), unitCost: n('5'), batchNumber: 'BATCH-UNIT', expirationDate: '2027-12-31' }
export const lot: LotStock = { inventoryLotId: id(30), businessProductId: id(10), batchNumber: 'BATCH-UNIT', expirationDate: '2027-12-31', quantityAvailableBase: n('20.000000000000') }
export const receiptRevision = (lines: PurchaseLine[]) => createHash('sha256').update(stringifyExactJson(lines)).digest('hex').toUpperCase()
export const json = (data: unknown, status = 200) => new Response(stringifyExactJson(data), { status, headers: { 'Content-Type': 'application/json' } })
export function operationServer() {
  const state = { detail: { revision: detail.revision, product: { ...detail.product }, units: [...detail.units] }, purchases: [] as Purchase[], lines: [] as PurchaseLine[], suppliers: [] as Supplier[], lots: [lot], expiring: [] as ExpiringLot[],
    loseCreate: false, loseSave: false, loseConfirm: false, confirmRemainsDraft: false, loseAdjustment: false, loseMinimum: false, failStock: false, failRead: false, failProducts: false, reject: 0 }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'; const url = new URL(path, 'https://unit.test')
    const offset = Number(url.searchParams.get('offset') ?? '0'); const limit = Number(url.searchParams.get('limit') ?? '20')
    if (path === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if ((method === 'POST' || method === 'PUT') && state.reject) return json({ code: 'unit.denied' }, state.reject)
    if (method === 'GET' && path.includes('/suppliers?')) return json(state.suppliers.slice(offset, offset + limit))
    if (method === 'POST' && path.endsWith('/suppliers')) {
      const data = parseExactJson(String(init?.body)) as SupplierInput
      const result = { ...data, id: id(40), createdAt: time, isActive: true }; state.suppliers.push(result); return json(result, 201)
    }
    if (path.includes('/products/search?')) return state.failProducts ? json({ code: 'unit.unavailable' }, 503) : json({ directMatches: [{ ...state.detail.product, businessProductId: id(10), quantityAvailableBase: n('0'), units: state.detail.units }], equivalentInBranch: [], otherBranchAvailability: [] })
    if (method === 'GET' && path.endsWith('/products/' + id(10))) return state.failProducts ? json({ code: 'unit.unavailable' }, 503) : json(state.detail)
    if (method === 'POST' && path.endsWith('/purchases')) {
      const data = parseExactJson(String(init?.body)) as { supplierId: string | null; documentReference: string | null }
      const result = { ...purchase, ...data }; state.purchases.push(result)
      if (state.loseCreate) throw new TypeError('Unit response loss')
      return json(result, 201)
    }
    if (path.includes('/purchases/search?')) return json(state.purchases.filter(row => row.documentReference === url.searchParams.get('reference')).slice(offset, offset + limit))
    if (method === 'GET' && path.includes('/purchases/')) {
      if (state.failRead) return json({ code: 'unit.unavailable' }, 503)
      const result = state.purchases.find(row => path.endsWith('/' + row.id))
      return result ? json({ purchase: result, revision: receiptRevision(state.lines), lines: state.lines }) : json({ code: 'purchase.not_found' }, 404)
    }
    if (method === 'PUT' && path.endsWith('/lines')) {
      const { lines, expectedRevision } = parseExactJson(String(init?.body)) as { lines: PurchaseLineInput[]; expectedRevision: string }
      if (expectedRevision !== receiptRevision(state.lines)) return json({ code: 'purchasing.receipt_changed' }, 409)
      state.lines = lines.map((row, index) => { const unit = state.detail.units.find(unit => unit.id === row.productUnitId)!; return { ...storedLine, ...row, id: id(21 + index), unitNameSnapshot: unit.name, conversionToBaseSnapshot: unit.conversionToBase, baseQuantity: multiply(row.quantity, unit.conversionToBase) } })
      if (state.loseSave) throw new TypeError('Unit response loss')
      return json(state.lines)
    }
    if (method === 'POST' && path.endsWith('/confirm')) {
      if ((parseExactJson(String(init?.body)) as { expectedRevision: string }).expectedRevision !== receiptRevision(state.lines)) return json({ code: 'purchasing.receipt_changed' }, 409)
      if (!state.confirmRemainsDraft) { state.purchases[0] = { ...state.purchases[0]!, status: 'confirmed', confirmedAt: time, confirmedByActorId: id(1) } }
      if (state.loseConfirm) throw new TypeError('Unit response loss')
      return json(state.purchases[0])
    }
    if (path.includes('/inventory/stock?')) return state.failStock ? json({ code: 'unit.unavailable' }, 503) : json(state.lots.map(row => ({ businessProductId: row.businessProductId, quantityAvailableBase: row.quantityAvailableBase })).slice(offset, offset + limit))
    if (path.includes('/inventory/lots?')) return json(state.lots.slice(offset, offset + limit))
    if (path.includes('/inventory/expiring-lots?')) return json(state.expiring.slice(offset, offset + limit))
    if (method === 'POST' && path.endsWith('/adjustments')) {
      const { quantityDeltaBase } = parseExactJson(String(init?.body)) as { quantityDeltaBase: JsonNumber }
      const before = state.lots[0]!.quantityAvailableBase; const after = add(before, quantityDeltaBase)
      state.lots = [{ ...state.lots[0]!, quantityAvailableBase: after }]
      if (state.loseAdjustment) throw new TypeError('Unit response loss')
      return json({ inventoryLotId: lot.inventoryLotId, stockMovementId: id(31), before, after })
    }
    if (method === 'PUT' && path.endsWith('/stock-threshold')) {
      const { minimumStockBase } = parseExactJson(String(init?.body)) as { minimumStockBase: JsonNumber }
      if (state.loseMinimum) throw new TypeError('Unit response loss')
      return json({ id: id(32), tenantId: id(2), branchId: id(5), businessProductId: id(10), minimumStockBase, updatedAt: time, updatedByActorId: id(1) })
    }
    throw new Error('Unexpected operations unit-test route: ' + method + ' ' + path)
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
export function mountOperation(fake: ReturnType<typeof operationServer>, page: ReactNode, search = '', context = workspace) {
  function Location() { return <output data-testid="location">{useLocation().search}</output> }
  const http = new HttpClient(fake.fetcher)
  const tree = (current: Workspace) => <RuntimeProvider runtime={{ service: createPreviewService('owner'), http, preview: false }}><MemoryRouter initialEntries={['/operations' + search]}><WorkspaceContext.Provider value={current}>{page}<Location /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>
  const view = render(tree(context)); return { ...view, changeContext: (current: Workspace) => view.rerender(tree(current)) }
}
