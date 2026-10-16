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
export interface SaleLineInput { businessProductId: string; productUnitId: string; quantity: JsonNumber; priceKind: 'retail' | 'wholesale' }
export type PaymentMethod = 'cash' | 'yape' | 'plin' | 'card' | 'transfer'
export interface SalePaymentInput { method: PaymentMethod; amount: JsonNumber }
export interface ConfirmSaleResult { saleId: string; totalAmount: JsonNumber; confirmedAt: string; version: number; payments: (SalePaymentInput & { salePaymentId: string })[] }
const confirmation = object<ConfirmSaleResult>({ saleId: uuid, totalAmount: decimal, confirmedAt: utc, version: uint,
  payments: array(object({ salePaymentId: uuid, method: oneOf('cash', 'yape', 'plin', 'card', 'transfer'), amount: decimal })) })
export const saleState = object<SaleState>({ saleId: uuid, tenantId: uuid, branchId: uuid, sellerMembershipId: uuid, cashSessionId: uuid,
  status: oneOf('draft', 'confirmed', 'voided'), totalAmount: decimal, createdAt: utc, updatedAt: utc, version: uint,
  lines: array(object<SaleLine>({ saleLineId: uuid, businessProductId: uuid, productUnitIdSnapshot: uuid, productNameSnapshot: text,
    quantity: decimal, baseQuantity: decimal, unitNameSnapshot: text, conversionToBaseSnapshot: decimal,
    priceKind: oneOf('retail', 'wholesale'), unitPriceSnapshot: decimal, lineTotal: decimal })) })
export function salesApi(http: HttpClient) {
  function decode(data: unknown, scope: BranchScope, saleId?: string) {
    const result = saleState(data)
    requireScope(result, scope)
    if (saleId && result.saleId.toLowerCase() !== saleId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
    return result
  }
  return {
    async void(scope: BranchScope, saleId: string, expectedVersion: number, reason: string, signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}/void`, { method: 'POST', body: { expectedVersion, reason }, signal, exactNumbers: true })
      try {
        const result = object({ saleId: uuid, voidedAt: utc, version: uint, paymentReversalCount: uint, stockReversalCount: uint })(data)
        if (result.saleId.toLowerCase() !== saleId.toLowerCase()) throw new Error('Unexpected resource')
        return result
      } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async confirm(scope: BranchScope, saleId: string, expectedVersion: number, payments: SalePaymentInput[], signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}/confirm`, {
        method: 'POST', body: { expectedVersion, payments }, signal, exactNumbers: true,
      })
      try {
        const result = confirmation(data)
        if (result.saleId.toLowerCase() !== saleId.toLowerCase()) throw new Error('Unexpected resource')
        return result
      } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async state(scope: BranchScope, saleId: string, signal?: AbortSignal) {
      return decode(await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}`, { signal, exactNumbers: true }), scope, saleId)
    },
    async draft(scope: BranchScope, saleId: string, signal?: AbortSignal) {
      const result = decode(await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}/draft`, { signal, exactNumbers: true }), scope, saleId)
      if (result.status !== 'draft') throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async create(scope: BranchScope, signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/sales`, { method: 'POST', body: {}, signal, exactNumbers: true })
      try { return decode(data, scope) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async replace(scope: BranchScope, saleId: string, expectedVersion: number, lines: SaleLineInput[], signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}/lines`, {
        method: 'PUT', body: { expectedVersion, lines }, signal, exactNumbers: true,
      })
      try { return decode(data, scope, saleId) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
