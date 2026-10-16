import { ApiError, HttpClient } from '../../shared/api/http'
import { array, dateOnly, decimal, nullable, object, oneOf, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { JsonNumber } from '../../shared/api/exactJson'
import { compare } from '../../shared/api/decimal'

// TransferEndpoints / TransferContracts / GetTransfersHandler. No optimistic version or idempotency key.
export const states = ['requested', 'approved', 'in_transit', 'received', 'cancelled'] as const
export type TransferState = typeof states[number]
export const stateLabel: Record<TransferState, string> = { requested: 'Solicitada', approved: 'Aprobada', in_transit: 'EN TRÁNSITO', received: 'RECIBIDA', cancelled: 'CANCELADA' }
export type GoodsAction = 'create' | 'approve' | 'cancel' | 'dispatch' | 'receive'
export interface TransferSummary { id: string; sourceBranchId: string; sourceBranchName: string; destinationBranchId: string; destinationBranchName: string; status: TransferState; requestedAt: string; updatedAt: string }
export interface TransferLine { id: string; businessProductId: string; productUnitIdSnapshot: string; requestedQuantity: JsonNumber; requestedBaseQuantity: JsonNumber; unitNameSnapshot: string; conversionToBaseSnapshot: JsonNumber }
export interface TransferEvent { id: string; eventType: 'requested' | 'approved' | 'dispatched' | 'received' | 'cancelled'; actorId: string; occurredAt: string; reason: string | null }
export interface TransferAllocation { id: string; transferLineId: string; sourceInventoryLotId: string; sourcePurchaseLineId: string; businessProductId: string; batchNumber: string | null; expirationDate: string | null; dispatchedQuantityBase: JsonNumber; receivedQuantityBase: JsonNumber | null; differenceBase: JsonNumber | null }
export interface Transfer extends TransferSummary { tenantId: string; lines: TransferLine[]; events: TransferEvent[]; allocations: TransferAllocation[] }
export interface TransferLineInput { businessProductId: string; productUnitId: string; quantity: JsonNumber }
export interface DispatchSelection { transferLineId: string; inventoryLotId: string; quantityBase: JsonNumber }
export interface ReceiptSelection { allocationId: string; quantityBase: JsonNumber }
const summaryShape = { id: uuid, sourceBranchId: uuid, sourceBranchName: text, destinationBranchId: uuid, destinationBranchName: text, status: oneOf(...states), requestedAt: utc, updatedAt: utc }
const summary = object<TransferSummary>(summaryShape)
const detail = object<Transfer>({ ...summaryShape, tenantId: uuid,
  lines: array(object<TransferLine>({ id: uuid, businessProductId: uuid, productUnitIdSnapshot: uuid, requestedQuantity: decimal, requestedBaseQuantity: decimal, unitNameSnapshot: text, conversionToBaseSnapshot: decimal })),
  events: array(object<TransferEvent>({ id: uuid, eventType: oneOf('requested', 'approved', 'dispatched', 'received', 'cancelled'), actorId: uuid, occurredAt: utc, reason: nullable(text) })),
  allocations: array(object<TransferAllocation>({ id: uuid, transferLineId: uuid, sourceInventoryLotId: uuid, sourcePurchaseLineId: uuid, businessProductId: uuid, batchNumber: nullable(text), expirationDate: nullable(dateOnly), dispatchedQuantityBase: decimal, receivedQuantityBase: nullable(decimal), differenceBase: nullable(decimal) })),
})
export const equalId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
export function pageQuery(offset: number, limit: number) {
  if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid transfer page')
  return new URLSearchParams({ offset: String(offset), limit: String(limit) })
}
function scoped(data: unknown, scope: BranchScope, id?: string) {
  const result = detail(data)
  if (!equalId(result.tenantId, scope.tenantId) || id && !equalId(result.id, id) || equalId(result.sourceBranchId, result.destinationBranchId) ||
    result.lines.length < 1 || result.lines.length > 200 || result.allocations.length > 1000 || new Set(result.lines.map(row => row.id.toLowerCase())).size !== result.lines.length ||
    result.allocations.some(row => !result.lines.some(line => equalId(line.id, row.transferLineId) && equalId(line.businessProductId, row.businessProductId)))) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function transfersApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}/transfers`
  async function post(scope: BranchScope, path: string, body: unknown, signal?: AbortSignal, id?: string) {
    const data = await http.request(root(scope) + path, { method: 'POST', body, signal, exactNumbers: true })
    try { return scoped(data, scope, id) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async list(scope: BranchScope, branchId: string | undefined, status: TransferState | undefined, offset = 0, signal?: AbortSignal) {
      const query = pageQuery(offset, 20); if (branchId) query.set('branchId', uuid(branchId)); if (status) query.set('status', oneOf(...states)(status))
      const result = array(summary)(await http.request(`${root(scope)}?${query}`, { signal, exactNumbers: true }))
      if (result.length > 20 || result.some(row => status && row.status !== status || branchId && !equalId(row.sourceBranchId, branchId) && !equalId(row.destinationBranchId, branchId))) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async read(scope: BranchScope, id: string, signal?: AbortSignal) { return scoped(await http.request(`${root(scope)}/${uuid(id)}`, { signal, exactNumbers: true }), scope, id) },
    async create(scope: BranchScope, sourceBranchId: string, destinationBranchId: string, lines: TransferLineInput[], signal?: AbortSignal) {
      const result = await post(scope, '', { sourceBranchId: uuid(sourceBranchId), destinationBranchId: uuid(destinationBranchId), lines }, signal)
      if (!equalId(result.sourceBranchId, sourceBranchId) || !equalId(result.destinationBranchId, destinationBranchId) || result.lines.length !== lines.length || lines.some(line => !result.lines.some(row => equalId(row.businessProductId, line.businessProductId) && equalId(row.productUnitIdSnapshot, line.productUnitId) && compare(row.requestedQuantity, line.quantity) === 0))) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      return result
    },
    approve: (scope: BranchScope, id: string, signal?: AbortSignal) => post(scope, `/${uuid(id)}/approve`, {}, signal, id),
    cancel: (scope: BranchScope, id: string, reason: string, signal?: AbortSignal) => post(scope, `/${uuid(id)}/cancel`, { reason }, signal, id),
    dispatch: (scope: BranchScope, id: string, allocations: DispatchSelection[], signal?: AbortSignal) => post(scope, `/${uuid(id)}/dispatch`, { allocations }, signal, id),
    receive: (scope: BranchScope, id: string, quantities: ReceiptSelection[], signal?: AbortSignal) => post(scope, `/${uuid(id)}/receive`, { quantities }, signal, id),
  }
}
