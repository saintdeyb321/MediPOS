import { HttpClient } from '../../shared/api/http'
import { array, branchPath, decimal, object, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: Inventory/InventoryDirectoryQueries.cs, ProductStockDetails.
export interface ProductStock { businessProductId: string; quantityAvailableBase: JsonNumber }
const stock = array(object<ProductStock>({ businessProductId: uuid, quantityAvailableBase: decimal }))
export function inventoryApi(http: HttpClient) {
  return { async stock(scope: BranchScope, productId: string | undefined, offset = 0, limit = 50, signal?: AbortSignal) {
    if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid paging')
    const query = new URLSearchParams({ offset: String(offset), limit: String(limit) })
    if (productId !== undefined) query.set('productId', uuid(productId))
    return stock(await http.request(`${branchPath(scope)}/inventory/stock?${query}`, { signal, exactNumbers: true }))
  } }
}
