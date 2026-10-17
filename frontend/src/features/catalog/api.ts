import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, decimal, nullable, object, oneOf, requireScope, text, uint, utc, uuid } from '../../shared/api/contracts'
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

export interface MedicineInput { components: { ingredient: string; strengthNormalized: string }[]; dosageForm: string; route: string | null; sanitaryRegistration: string | null }
export interface Medicine extends MedicineInput { activeIngredients: string[]; normalizedStrength: string }
export interface Category { id: string; name: string; isActive: boolean; createdAt: string }
export interface GlobalProduct { id: string; productType: 'medicine' | 'retail'; name: string; categoryId: string; brandOrLaboratory: string; barcode: string | null; medicine: Medicine | null }
export interface BusinessProduct extends GlobalProduct { tenantId: string; globalProductId: string | null; internalCode: string; retailPrice: JsonNumber; wholesalePrice: JsonNumber | null; isActive: boolean; createdAt: string }
export interface ProductDetail { product: BusinessProduct; units: ProductUnit[] }
export interface PricesInput { retailPrice: JsonNumber; wholesalePrice: JsonNumber | null }
export interface LocalProductInput extends PricesInput { internalCode: string; productType: 'medicine' | 'retail'; name: string; categoryId: string; brandOrLaboratory: string; barcode: string | null; medicine: MedicineInput | null; isActive: boolean }
export interface FromGlobalInput extends PricesInput { globalProductId: string; internalCode: string; isActive: boolean }
export type UnitInput = Omit<ProductUnit, 'id'>
export interface UnitConfiguration { version: number; units: ProductUnit[] }
export type UnitChange = UnitInput & { id: string | null }
const unitConfiguration = object<UnitConfiguration>({ version: uint, units: array(productUnit) })
const medicine = nullable(object<Medicine>({ components: array(object({ ingredient: text, strengthNormalized: text })), dosageForm: text,
  route: nullable(text), sanitaryRegistration: nullable(text), activeIngredients: array(text), normalizedStrength: text }))
const globalShape = { id: uuid, productType: oneOf('medicine', 'retail'), name: text, categoryId: uuid, brandOrLaboratory: text, barcode: nullable(text), medicine }
export const businessProduct = object<BusinessProduct>({ ...globalShape, tenantId: uuid, globalProductId: nullable(uuid), internalCode: text,
  retailPrice: decimal, wholesalePrice: nullable(decimal), isActive: boolean, createdAt: utc })
const detail = object<ProductDetail>({ product: businessProduct, units: array(productUnit) })
const category = object<Category>({ id: uuid, name: text, isActive: boolean, createdAt: utc })
const globalProduct = object<GlobalProduct>(globalShape)
function page(offset: number, limit: number) {
  if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid catalog page')
  return new URLSearchParams({ offset: String(offset), limit: String(limit) })
}
function scopedProduct(data: unknown, scope: BranchScope, id?: string) {
  const result = businessProduct(data)
  requireScope({ tenantId: result.tenantId, branchId: scope.branchId }, scope)
  if (id && result.id.toLowerCase() !== id.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function catalogApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}`
  async function write(scope: BranchScope, path: string, body: unknown, method: 'POST' | 'PUT', signal?: AbortSignal, id?: string) {
    const data = await http.request(root(scope) + path, { method, body, signal, exactNumbers: true })
    try { return scopedProduct(data, scope, id) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async list(scope: BranchScope, offset = 0, limit = 50, signal?: AbortSignal) {
      const data = array(businessProduct)(await http.request(`${root(scope)}/products?${page(offset, limit)}`, { signal, exactNumbers: true }))
      return data.map(item => scopedProduct(item, scope))
    },
    async categories(scope: BranchScope, offset = 0, limit = 100, signal?: AbortSignal) {
      return array(category)(await http.request(`${root(scope)}/categories?${page(offset, limit)}`, { signal, exactNumbers: true }))
    },
    async global(scope: BranchScope, offset = 0, limit = 50, signal?: AbortSignal) {
      return array(globalProduct)(await http.request(`${root(scope)}/global-products?${page(offset, limit)}`, { signal, exactNumbers: true }))
    },
    async detail(scope: BranchScope, id: string, signal?: AbortSignal) {
      const result = detail(await http.request(`${branchPath(scope)}/products/${uuid(id)}`, { signal, exactNumbers: true }))
      scopedProduct(result.product, scope, id); return result
    },
    create: (scope: BranchScope, input: LocalProductInput, signal?: AbortSignal) => write(scope, '/products/local', input, 'POST', signal),
    fromGlobal: (scope: BranchScope, input: FromGlobalInput, signal?: AbortSignal) => write(scope, '/products/from-global', input, 'POST', signal),
    prices: (scope: BranchScope, id: string, input: PricesInput, signal?: AbortSignal) => write(scope, `/products/${uuid(id)}/prices`, input, 'PUT', signal, id),
    status: (scope: BranchScope, id: string, isActive: boolean, signal?: AbortSignal) => write(scope, `/products/${uuid(id)}/status`, { isActive }, 'PUT', signal, id),
    async unitConfiguration(scope: BranchScope, id: string, signal?: AbortSignal) {
      return unitConfiguration(await http.request(`${root(scope)}/products/${uuid(id)}/unit-configuration`, { signal, exactNumbers: true }))
    },
    async configureUnits(scope: BranchScope, id: string, expectedVersion: number, units: UnitChange[], signal?: AbortSignal) {
      if (!Number.isInteger(expectedVersion) || expectedVersion <= 0 || expectedVersion > 4294967295) throw new ApiError(400, 'request.invalid')
      const data = await http.request(`${root(scope)}/products/${uuid(id)}/unit-configuration`, { method: 'PUT', body: { expectedVersion, units }, signal, exactNumbers: true })
      try { return unitConfiguration(data) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async initializeUnits(_scope: BranchScope, _id: string, _units: UnitInput[], _signal?: AbortSignal): Promise<ProductUnit[]> {
      void [_scope, _id, _units, _signal]
      // Compatibility guard: callers must obtain a version and use configureUnits. Never fallback to old PUT.
      throw new ApiError(409, 'catalog.units_legacy_write_blocked')
    },
    async search(scope: BranchScope, query: string, limit = 20, signal?: AbortSignal) {
    if (!Number.isInteger(limit) || limit < 1 || limit > 50) throw new RangeError('Search limit must be 1–50')
    const parameters = new URLSearchParams({ q: query, limit: String(limit) })
    return productSearch(await http.request(`${branchPath(scope)}/products/search?${parameters}`, { signal, exactNumbers: true }))
  } }
}
