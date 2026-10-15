import { HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, decimal, nullable, object, oneOf, text, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: SearchProducts/IProductSearchStore.cs, ReplaceProductUnits/ProductUnitDetails.
export interface ProductUnit { id: string; name: string; conversionToBase: JsonNumber; isBaseUnit: boolean; isActive: boolean }
export const productUnit = object<ProductUnit>({ id: uuid, name: text, conversionToBase: decimal, isBaseUnit: boolean, isActive: boolean })
export interface ProductMatch {
  businessProductId: string; name: string; productType: 'medicine' | 'retail'; brandOrLaboratory: string
  internalCode: string; barcode: string | null; retailPrice: JsonNumber; wholesalePrice: JsonNumber | null
  quantityAvailableBase: JsonNumber; units: ProductUnit[]
}
const product = object<ProductMatch>({ businessProductId: uuid, name: text, productType: oneOf('medicine', 'retail'), brandOrLaboratory: text,
  internalCode: text, barcode: nullable(text), retailPrice: decimal, wholesalePrice: nullable(decimal), quantityAvailableBase: decimal, units: array(productUnit) })
export interface ProductSearch {
  directMatches: ProductMatch[]
  equivalentInBranch: { product: ProductMatch; forDirectProducts: string[]; description: string }[]
  otherBranchAvailability: { branchId: string; branchName: string; businessProductId: string; quantityAvailableBase: JsonNumber }[]
}
export const productSearch = object<ProductSearch>({ directMatches: array(product),
  equivalentInBranch: array(object({ product, forDirectProducts: array(uuid), description: text })),
  otherBranchAvailability: array(object({ branchId: uuid, branchName: text, businessProductId: uuid, quantityAvailableBase: decimal })) })
export function catalogApi(http: HttpClient) {
  return { async search(scope: BranchScope, query: string, limit = 20, signal?: AbortSignal) {
    if (!Number.isInteger(limit) || limit < 1 || limit > 50) throw new RangeError('Search limit must be 1–50')
    const parameters = new URLSearchParams({ q: query, limit: String(limit) })
    return productSearch(await http.request(`${branchPath(scope)}/products/search?${parameters}`, { signal, exactNumbers: true }))
  } }
}
