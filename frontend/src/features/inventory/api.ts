import { recoverOperation } from '../../shared/api/operations'
import type { OperationIntention } from '../../shared/api/operations'
import { ApiError, HttpClient } from '../../shared/api/http'
import { array, branchPath, dateOnly, decimal, nullable, object, requireScope, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: Inventory/InventoryDirectoryQueries.cs, ProductStockDetails.
export interface ProductStock { businessProductId: string; quantityAvailableBase: JsonNumber }
const stock = array(object<ProductStock>({ businessProductId: uuid, quantityAvailableBase: decimal }))
export interface LotStock { inventoryLotId: string; businessProductId: string; batchNumber: string | null; expirationDate: string | null; quantityAvailableBase: JsonNumber }
export interface ExpiringLot { inventoryLotId: string; branchId: string; businessProductId: string; productName: string; quantityAvailableBase: JsonNumber; expirationDate: string; costValue: JsonNumber | null; potentialSaleValue: JsonNumber }
export interface Adjustment { inventoryLotId: string; stockMovementId: string; before: JsonNumber; after: JsonNumber }
export interface StockThreshold { id: string; tenantId: string; branchId: string; businessProductId: string; minimumStockBase: JsonNumber; updatedAt: string; updatedByActorId: string }
const lots = array(object<LotStock>({ inventoryLotId: uuid, businessProductId: uuid, batchNumber: nullable(text), expirationDate: nullable(dateOnly), quantityAvailableBase: decimal }))
const expiring = array(object<ExpiringLot>({ inventoryLotId: uuid, branchId: uuid, businessProductId: uuid, productName: text, quantityAvailableBase: decimal, expirationDate: dateOnly, costValue: nullable(decimal), potentialSaleValue: decimal }))
const adjustment = object<Adjustment>({ inventoryLotId: uuid, stockMovementId: uuid, before: decimal, after: decimal })
const threshold = object<StockThreshold>({ id: uuid, tenantId: uuid, branchId: uuid, businessProductId: uuid, minimumStockBase: decimal, updatedAt: utc, updatedByActorId: uuid })
export function inventoryApi(http: HttpClient) {
  async function read<T>(scope: BranchScope, resource: string, decode: (data: unknown) => T, productId: string | undefined, offset: number, limit: number, signal?: AbortSignal) {
    if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid paging')
    const query = new URLSearchParams({ offset: String(offset), limit: String(limit) })
    if (productId !== undefined) query.set('productId', uuid(productId))
    return decode(await http.request(`${branchPath(scope)}/inventory/${resource}?${query}`, { signal, exactNumbers: true }))
  }
  async function write<T>(path: string, method: 'POST' | 'PUT', body: unknown, decode: (data: unknown) => T, signal?: AbortSignal, intention?: OperationIntention) {
    const data = await http.request(path, { method, body, signal, exactNumbers: true, intention })
    try { return decode(data) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async recover(scope: BranchScope, id: string, kind: 'adjustment' | 'minimum', signal?: AbortSignal) {
      const data = await recoverOperation(http, scope, id, kind, signal)
      if (kind === 'adjustment') return adjustment(data)
      const result = threshold(data); requireScope(result, scope); return result
    },
    stock: (scope: BranchScope, productId: string | undefined, offset = 0, limit = 50, signal?: AbortSignal) => read(scope, 'stock', stock, productId, offset, limit, signal),
    lots: (scope: BranchScope, productId: string | undefined, offset = 0, limit = 20, signal?: AbortSignal) => read(scope, 'lots', lots, productId, offset, limit, signal),
    async expiring(scope: BranchScope, productId: string | undefined, offset = 0, limit = 20, signal?: AbortSignal) {
      const result = await read(scope, 'expiring-lots', expiring, productId, offset, limit, signal)
      result.forEach(row => requireScope({ tenantId: scope.tenantId, branchId: row.branchId }, scope)); return result
    },
    adjust: (scope: BranchScope, lotId: string, quantityDeltaBase: JsonNumber, reason: string, signal?: AbortSignal, intention?: OperationIntention) =>
      write(`/api/tenants/${uuid(scope.tenantId)}/inventory/lots/${uuid(lotId)}/adjustments`, 'POST', { quantityDeltaBase, reason }, data => {
        const result = adjustment(data)
        if (result.inventoryLotId.toLowerCase() !== lotId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
        return result
      }, signal, intention),
    minimum: (scope: BranchScope, productId: string, minimumStockBase: JsonNumber, signal?: AbortSignal, intention?: OperationIntention) =>
      write(`${branchPath(scope)}/products/${uuid(productId)}/stock-threshold`, 'PUT', { minimumStockBase }, data => {
        const result = threshold(data); requireScope(result, scope)
        if (result.businessProductId.toLowerCase() !== productId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
        return result
      }, signal, intention),
  }
}
