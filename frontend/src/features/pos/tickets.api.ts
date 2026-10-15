import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, decimal, nullable, object, oneOf, requireScope, text, uint, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: SalesPos/InternalTicket.cs. Display values are current, not transaction snapshots.
export interface TicketDisplay {
  tenantId: string; businessName: string; legalEntityId: string; legalName: string; ruc: string
  branchId: string; branchName: string; sellerMembershipId: string; sellerUserId: string; sellerDisplayName: string
}
export interface InternalTicket {
  saleId: string; cashSessionId: string; ticketNumber: string; currentDisplayData: TicketDisplay
  saleDateTime: string; status: 'confirmed' | 'voided'; statusLabel: string; isVoided: boolean; voidedAt: string | null; voidReason: string | null
  lines: { productName: string; unitName: string; quantity: JsonNumber; baseQuantity: JsonNumber; conversionToBase: JsonNumber; unitPrice: JsonNumber; priceKind: string; lineTotal: JsonNumber; unitPriceBasis: 'BASE_UNIT' }[]
  payments: { method: string; amount: JsonNumber }[]; totalAmount: JsonNumber
  documentType: 'INTERNAL_TICKET'; footer: string; supportedPrintWidthsMm: number[]
}
export const internalTicket = object<InternalTicket>({ saleId: uuid, cashSessionId: uuid, ticketNumber: text,
  currentDisplayData: object<TicketDisplay>({ tenantId: uuid, businessName: text, legalEntityId: uuid, legalName: text, ruc: text,
    branchId: uuid, branchName: text, sellerMembershipId: uuid, sellerUserId: uuid, sellerDisplayName: text }),
  saleDateTime: utc, status: oneOf('confirmed', 'voided'), statusLabel: text, isVoided: boolean, voidedAt: nullable(utc), voidReason: nullable(text),
  lines: array(object({ productName: text, unitName: text, quantity: decimal, baseQuantity: decimal, conversionToBase: decimal, unitPrice: decimal,
    priceKind: text, lineTotal: decimal, unitPriceBasis: oneOf('BASE_UNIT') })),
  payments: array(object({ method: text, amount: decimal })), totalAmount: decimal, documentType: oneOf('INTERNAL_TICKET'), footer: text, supportedPrintWidthsMm: array(uint) })
export function ticketsApi(http: HttpClient) {
  return { async read(scope: BranchScope, saleId: string, signal?: AbortSignal) {
    const result = internalTicket(await http.request(`${branchPath(scope)}/sales/${uuid(saleId)}/ticket`, { signal, exactNumbers: true }))
    requireScope(result.currentDisplayData, scope)
    if (result.saleId.toLowerCase() !== saleId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
    return result
  } }
}
