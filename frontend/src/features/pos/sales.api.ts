import { ApiError, HttpClient } from '../../shared/api/http'
import { array, branchPath, decimal, object, oneOf, requireScope, uint, utc, uuid } from '../../shared/api/contracts'
import { text } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: SalesPos/SaleDraftContracts.cs; read persisted state for response-loss reconciliation.
export interface SaleLine {
  saleLineId: string; businessProductId: string; productUnitIdSnapshot: string; productNameSnapshot: string
  quantity: JsonNumber; baseQuantity: JsonNumber; unitNameSnapshot: string; conversionToBaseSnapshot: JsonNumber
  priceKind: 'retail' | 'wholesale'; unitPriceSnapshot: JsonNumber; lineTotal: JsonNumber
}
export interface SaleState {
  saleId: string; tenantId: string; branchId: string; sellerMembershipId: string; cashSessionId: string
  status: 'draft' | 'confirmed' | 'voided'; totalAmount: JsonNumber; createdAt: string; updatedAt: string; version: number; lines: SaleLine[]
}
export const saleState = object<SaleState>({ saleId: uuid, tenantId: uuid, branchId: uuid, sellerMembershipId: uuid, cashSessionId: uuid,
  status: oneOf('draft', 'confirmed', 'voided'), totalAmount: decimal, createdAt: utc, updatedAt: utc, version: uint,
  lines: array(object<SaleLine>({ saleLineId: uuid, businessProductId: uuid, productUnitIdSnapshot: uuid, productNameSnapshot: text,
    quantity: decimal, baseQuantity: decimal, unitNameSnapshot: text, conversionToBaseSnapshot: decimal,
    priceKind: oneOf('retail', 'wholesale'), unitPriceSnapshot: decimal, lineTotal: decimal })) })
export function salesApi(http: HttpClient) {
  return { async state(scope: BranchScope, saleId: string, signal?: AbortSignal) {
    const result = saleState(await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}`, { signal, exactNumbers: true }))
    requireScope(result, scope)
    if (result.saleId.toLowerCase() !== saleId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
    return result
  } }
}
