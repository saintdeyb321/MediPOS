import { ApiError, HttpClient } from '../../shared/api/http'
import { branchPath, decimal, nullable, object, oneOf, requireScope, utc, uuid } from '../../shared/api/contracts'
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
export function cashApi(http: HttpClient) {
  return {
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
