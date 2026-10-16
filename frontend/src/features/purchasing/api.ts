import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, dateOnly, decimal, nullable, object, oneOf, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// PurchasingEndpoints + PurchaseDirectoryQueries + ReplacePurchaseLinesHandler. No general purchase directory.
export interface Supplier { id: string; name: string; ruc: string | null; contact: string | null; isActive: boolean; createdAt: string }
export interface SupplierInput { name: string; ruc: string | null; contact: string | null }
export interface Purchase { id: string; branchId: string; supplierId: string | null; documentReference: string | null; status: 'draft' | 'confirmed'; createdAt: string; confirmedAt: string | null; createdByActorId: string; confirmedByActorId: string | null }
export interface PurchaseLineInput { businessProductId: string; productUnitId: string; quantity: JsonNumber; unitCost: JsonNumber; batchNumber: string | null; expirationDate: string | null }
export interface PurchaseLine { id: string; businessProductId: string; quantity: JsonNumber; unitNameSnapshot: string; conversionToBaseSnapshot: JsonNumber; baseQuantity: JsonNumber; unitCost: JsonNumber; batchNumber: string | null; expirationDate: string | null }
export interface PurchaseView { purchase: Purchase; lines: PurchaseLine[] }
const supplier = object<Supplier>({ id: uuid, name: text, ruc: nullable(text), contact: nullable(text), isActive: boolean, createdAt: utc })
const purchase = object<Purchase>({ id: uuid, branchId: uuid, supplierId: nullable(uuid), documentReference: nullable(text), status: oneOf('draft', 'confirmed'), createdAt: utc, confirmedAt: nullable(utc), createdByActorId: uuid, confirmedByActorId: nullable(uuid) })
const line = object<PurchaseLine>({ id: uuid, businessProductId: uuid, quantity: decimal, unitNameSnapshot: text, conversionToBaseSnapshot: decimal, baseQuantity: decimal, unitCost: decimal, batchNumber: nullable(text), expirationDate: nullable(dateOnly) })
export function purchasePage(offset: number, limit: number) {
  if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid page')
  return new URLSearchParams({ offset: String(offset), limit: String(limit) })
}
function scoped(data: unknown, scope: BranchScope, id?: string) {
  const result = purchase(data)
  if (result.branchId.toLowerCase() !== uuid(scope.branchId).toLowerCase() || id && result.id.toLowerCase() !== uuid(id).toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
  return result
}
async function write<T>(http: HttpClient, path: string, method: 'POST' | 'PUT', body: unknown, decode: (data: unknown) => T, signal?: AbortSignal) {
  const data = await http.request(path, { method, body, signal, exactNumbers: true })
  try { return decode(data) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
}
export function purchasingApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}`
  return {
    async suppliers(scope: BranchScope, offset = 0, limit = 20, signal?: AbortSignal) {
      return array(supplier)(await http.request(`${branchPath(scope)}/suppliers?${purchasePage(offset, limit)}`, { signal, exactNumbers: true }))
    },
    createSupplier: (scope: BranchScope, input: SupplierInput, signal?: AbortSignal) => write(http, root(scope) + '/suppliers', 'POST', input, supplier, signal),
    create: (scope: BranchScope, supplierId: string | null, documentReference: string | null, signal?: AbortSignal) =>
      write(http, branchPath(scope) + '/purchases', 'POST', { supplierId: supplierId === null ? null : uuid(supplierId), documentReference }, data => scoped(data, scope), signal),
    async read(scope: BranchScope, id: string, signal?: AbortSignal): Promise<PurchaseView> {
      const result = object({ purchase, lines: array(line) })(await http.request(`${branchPath(scope)}/purchases/${uuid(id)}`, { signal, exactNumbers: true }))
      scoped(result.purchase, scope, id); return result
    },
    lines: (scope: BranchScope, id: string, lines: PurchaseLineInput[], signal?: AbortSignal) =>
      write(http, `${root(scope)}/purchases/${uuid(id)}/lines`, 'PUT', { lines }, array(line), signal),
    confirm: (scope: BranchScope, id: string, signal?: AbortSignal) =>
      write(http, `${root(scope)}/purchases/${uuid(id)}/confirm`, 'POST', {}, data => scoped(data, scope, id), signal),
    async search(scope: BranchScope, reference: string, offset = 0, limit = 20, signal?: AbortSignal) {
      const query = purchasePage(offset, limit); query.set('reference', reference)
      return array(purchase)(await http.request(`${branchPath(scope)}/purchases/search?${query}`, { signal, exactNumbers: true })).map(row => scoped(row, scope))
    },
  }
}
