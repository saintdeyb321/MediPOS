import { ApiError, HttpClient } from '../../shared/api/http'
import { array, branchPath, decimal, nullable, object, oneOf, requireScope, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'

// Backend: Cash/FindOpenCashSession.cs and CashSessionStatusQueries.cs.
export interface OpenCashSession {
  cashSessionId: string; tenantId: string; branchId: string; membershipId: string
  openingAmount: JsonNumber; openedAt: string; openedByActorId: string
}
const openCashSession = object<OpenCashSession>({
  cashSessionId: uuid, tenantId: uuid, branchId: uuid, membershipId: uuid, openingAmount: decimal, openedAt: utc, openedByActorId: uuid,
})
export const currentCashSession = object<{ session: OpenCashSession | null }>({ session: nullable(openCashSession) })
export interface CashSessionStatus {
  cashSessionId: string; branchId: string; membershipId: string; status: 'open' | 'closed'
  openingAmount: JsonNumber; openedAt: string; closedAt: string | null; expectedCashAmount: JsonNumber | null
}
const cashSessionStatus = object<CashSessionStatus>({ cashSessionId: uuid, branchId: uuid, membershipId: uuid, status: oneOf('open', 'closed'),
  openingAmount: decimal, openedAt: utc, closedAt: nullable(utc), expectedCashAmount: nullable(decimal) })
export interface CashReconciliation {
  cashSessionId: string; tenantId: string
  employeeBranch: { branchId: string; branchName: string; membershipId: string; userId: string; displayName: string }
  openingAmount: JsonNumber; paymentTotals: { cash: JsonNumber; yape: JsonNumber; plin: JsonNumber; card: JsonNumber; transfer: JsonNumber }
  netSalesAmount: JsonNumber; expectedCashAmount: JsonNumber; countedCashAmount: JsonNumber; cashDifference: JsonNumber
  openedAt: string; closedAt: string; closedByActorId: string; cashTransfers: { incoming: JsonNumber; outgoing: JsonNumber }
}
const reconciliation = object<CashReconciliation>({ cashSessionId: uuid, tenantId: uuid,
  employeeBranch: object({ branchId: uuid, branchName: text, membershipId: uuid, userId: uuid, displayName: text }),
  openingAmount: decimal, paymentTotals: object({ cash: decimal, yape: decimal, plin: decimal, card: decimal, transfer: decimal }),
  netSalesAmount: decimal, expectedCashAmount: decimal, countedCashAmount: decimal, cashDifference: decimal,
  openedAt: utc, closedAt: utc, closedByActorId: uuid, cashTransfers: object({ incoming: decimal, outgoing: decimal }),
})
export interface ActiveCash { cashSessionId: string; branchId: string; branchName: string; membershipId: string; userId: string; displayName: string; openingAmount: JsonNumber; openedAt: string; accumulatedSales: JsonNumber }
const activeCash = array(object<ActiveCash>({ cashSessionId: uuid, branchId: uuid, branchName: text, membershipId: uuid, userId: uuid, displayName: text, openingAmount: decimal, openedAt: utc, accumulatedSales: decimal }))
function decodeReconciliation(data: unknown, scope: BranchScope, id: string) {
  const result = reconciliation(data)
  requireScope({ tenantId: result.tenantId, branchId: result.employeeBranch.branchId }, scope)
  if (result.cashSessionId.toLowerCase() !== id.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function cashApi(http: HttpClient) {
  return {
    async active(scope: BranchScope, offset = 0, signal?: AbortSignal) {
      if (!Number.isInteger(offset) || offset < 0 || offset > 100000) throw new RangeError('Invalid offset')
      const result = activeCash(await http.request(`/api/tenants/${uuid(scope.tenantId)}/cash-sessions/active?${new URLSearchParams({ branchId: uuid(scope.branchId), offset: String(offset), limit: '50' })}`, { signal, exactNumbers: true }))
      for (const row of result) if (row.branchId.toLowerCase() !== scope.branchId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async reconciliation(scope: BranchScope, id: string, signal?: AbortSignal) {
      return decodeReconciliation(await http.request(`${branchPath(scope)}/cash-sessions/${uuid(id)}/reconciliation`, { signal, exactNumbers: true }), scope, id)
    },
    async close(scope: BranchScope, id: string, countedCashAmount: JsonNumber, signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/cash-sessions/${uuid(id)}/close`, { method: 'POST', body: { countedCashAmount }, signal, exactNumbers: true })
      try { return decodeReconciliation(data, scope, id) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async open(scope: BranchScope, openingAmount: JsonNumber, signal?: AbortSignal) {
      const data = await http.request(`${branchPath(scope)}/cash-sessions`, { method: 'POST', body: { openingAmount }, signal, exactNumbers: true })
      try { const result = openCashSession(data); requireScope(result, scope); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async current(scope: BranchScope, signal?: AbortSignal) {
      const result = currentCashSession(await http.request(`${branchPath(scope)}/cash-sessions/current`, { signal, exactNumbers: true }))
      if (result.session) requireScope(result.session, scope)
      return result
    },
    async status(scope: BranchScope, sessionId: string, signal?: AbortSignal) {
      const result = cashSessionStatus(await http.request(`${branchPath(scope)}/cash-sessions/${uuid(sessionId)}`, { signal, exactNumbers: true }))
      requireScope({ tenantId: scope.tenantId, branchId: result.branchId }, scope)
      if (result.cashSessionId.toLowerCase() !== sessionId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
  }
}
