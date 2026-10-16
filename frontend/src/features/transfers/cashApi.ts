import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, branchPath, decimal, nullable, object, oneOf, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'
import { compare, fits, maximumMoney } from '../../shared/api/decimal'
import { equalId, pageQuery } from './api'

// CashTransferEndpoints and CashTransferContracts. Cash is separate from goods transfers.
export interface CashTransfer { id: string; tenantId: string; sourceBranchId: string; sourceCashSessionId: string; destinationBranchId: string; destinationCashSessionId: string | null; amount: JsonNumber; status: 'in_transit' | 'received'; dispatchedAt: string; dispatchedByActorId: string; receivedAt: string | null; receivedByActorId: string | null }
const detail = object<CashTransfer>({ id: uuid, tenantId: uuid, sourceBranchId: uuid, sourceCashSessionId: uuid, destinationBranchId: uuid, destinationCashSessionId: nullable(uuid), amount: decimal, status: oneOf('in_transit', 'received'), dispatchedAt: utc, dispatchedByActorId: uuid, receivedAt: nullable(utc), receivedByActorId: nullable(uuid) })
function scoped(data: unknown, scope: BranchScope, id?: string) {
  const result = detail(data)
  if (!equalId(result.tenantId, scope.tenantId) || id && !equalId(result.id, id) || !fits(result.amount, maximumMoney, 4) ||
    (result.status === 'received' ? !result.destinationCashSessionId || !result.receivedAt || !result.receivedByActorId || equalId(result.sourceCashSessionId, result.destinationCashSessionId) : result.destinationCashSessionId !== null || result.receivedAt !== null || result.receivedByActorId !== null)) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export const cashSettled = (value: CashTransfer, action: string) => action === 'create' || action === 'receive' && value.status === 'received'
export function cashTransfersApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}/cash-transfers`
  async function post(scope: BranchScope, path: string, body: unknown, signal?: AbortSignal, id?: string) {
    const data = await http.request(path, { method: 'POST', body, signal, exactNumbers: true })
    try { return scoped(data, scope, id) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async pending(scope: BranchScope, destination: string | undefined, offset = 0, signal?: AbortSignal) {
      const query = pageQuery(offset, 20); if (destination) query.set('destinationBranchId', uuid(destination))
      const result = object({ items: array(detail), hasMore: boolean })(await http.request(`${root(scope)}/pending?${query}`, { signal, exactNumbers: true }))
      if (result.items.length > 20 || result.items.some(row => { scoped(row, scope); return row.status !== 'in_transit' || destination && !equalId(row.destinationBranchId, destination) })) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async read(scope: BranchScope, id: string, signal?: AbortSignal) { return scoped(await http.request(`${root(scope)}/${uuid(id)}`, { signal, exactNumbers: true }), scope, id) },
    async dispatch(scope: BranchScope, sourceCashSessionId: string, destinationBranchId: string, amount: JsonNumber, signal?: AbortSignal) {
      const result = await post(scope, `${branchPath(scope)}/cash-transfers`, { sourceCashSessionId: uuid(sourceCashSessionId), destinationBranchId: uuid(destinationBranchId), amount }, signal)
      if (!equalId(result.sourceBranchId, scope.branchId) || !equalId(result.sourceCashSessionId, sourceCashSessionId) || !equalId(result.destinationBranchId, destinationBranchId) || compare(result.amount, amount) !== 0) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      return result
    },
    async receive(scope: BranchScope, id: string, destinationCashSessionId: string, signal?: AbortSignal) {
      const result = await post(scope, `${root(scope)}/${uuid(id)}/receive`, { destinationCashSessionId: uuid(destinationCashSessionId) }, signal, id)
      if (result.destinationCashSessionId && !equalId(result.destinationCashSessionId, destinationCashSessionId)) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      return result
    },
  }
}
