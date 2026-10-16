import type { OperationalAccess, Workspace } from '../access'
import { add, compare, decimalInput, fits, maximumQuantity, multiply, zero } from '../../shared/api/decimal'
import { JsonNumber } from '../../shared/api/exactJson'
import type { ProductDetail, ProductUnit } from '../catalog'
import type { LotStock } from '../inventory'
import { equalId } from './api'
import type { DispatchSelection, GoodsAction, ReceiptSelection, Transfer, TransferLineInput } from './api'

export const difference = (sent: JsonNumber, received: JsonNumber) => add(sent, new JsonNumber('-' + received.literal))
export const total = (values: readonly JsonNumber[]) => values.reduce(add, zero)
export function usableAccess(access: OperationalAccess | undefined, w: Workspace, branchId = w.branchId) {
  return access?.isAllowed === true && access.context != null && equalId(access.context.tenantId, w.tenantId) && access.context.branchId != null && equalId(access.context.branchId, branchId) && equalId(access.context.userId, w.session.userId) && equalId(access.context.membershipId, w.access.membershipId)
}
export function goodsPermissions(t: Transfer, w: Workspace, source?: OperationalAccess, destination?: OperationalAccess) {
  const src = usableAccess(source, w, t.sourceBranchId); const dst = usableAccess(destination, w, t.destinationBranchId)
  const sourceRole = source?.context?.role; const destRole = destination?.context?.role
  const requester = t.events.find(event => event.eventType === 'requested')?.actorId
  return {
    approve: t.status === 'requested' && src && (sourceRole === 'owner' || sourceRole === 'pharmacist'),
    dispatch: t.status === 'approved' && src && (sourceRole === 'owner' || sourceRole === 'pharmacist'),
    receive: t.status === 'in_transit' && dst && (destRole === 'owner' || destRole === 'pharmacist'),
    cancel: (t.status === 'requested' || t.status === 'approved') && (src && (sourceRole === 'owner' || sourceRole === 'pharmacist') || t.status === 'requested' && dst && destRole !== 'owner' && Boolean(requester && equalId(requester, w.session.userId))),
  }
}
export function goodsSettled(t: Transfer, action: string) {
  const event = ({ approve: 'approved', cancel: 'cancelled', dispatch: 'dispatched', receive: 'received' } as Record<string, string>)[action]
  return action === 'create' || Boolean(event && t.events.some(row => row.eventType === event))
}
export interface RequestLine { name: string; unit: string; base: JsonNumber; input: TransferLineInput }
export function requestLine(product: ProductDetail, unit: ProductUnit | undefined, text: string): RequestLine | null {
  const quantity = decimalInput(text, maximumQuantity, 12)
  if (!quantity || !product.product.isActive || !unit?.isActive) return null
  const base = multiply(quantity, unit.conversionToBase)
  if (!fits(base, maximumQuantity, 12)) return null
  return { name: product.product.name, unit: unit.name, base, input: { businessProductId: product.product.id, productUnitId: unit.id, quantity } }
}
export function validRequest(lines: readonly RequestLine[]) {
  return lines.length > 0 && lines.length <= 200 && new Set(lines.map(row => `${row.input.businessProductId.toLowerCase()}/${row.input.productUnitId.toLowerCase()}`)).size === lines.length
}
export interface LotSelection { lot: LotStock; input: DispatchSelection }
export function validDispatch(t: Transfer, rows: readonly LotSelection[]) {
  if (t.status !== 'approved' || rows.length < 1 || rows.length > 1000 || new Set(rows.map(row => row.input.transferLineId.toLowerCase() + '/' + row.input.inventoryLotId.toLowerCase())).size !== rows.length) return false
  for (const row of rows) {
    const line = t.lines.find(line => equalId(line.id, row.input.transferLineId))
    if (!line || !equalId(row.lot.inventoryLotId, row.input.inventoryLotId) || !equalId(row.lot.businessProductId, line.businessProductId) || !fits(row.input.quantityBase, maximumQuantity, 12)) return false
  }
  return t.lines.every(line => compare(total(rows.filter(row => equalId(row.input.transferLineId, line.id)).map(row => row.input.quantityBase)), line.requestedBaseQuantity) === 0) &&
    rows.every(row => compare(total(rows.filter(item => equalId(item.input.inventoryLotId, row.input.inventoryLotId)).map(item => item.input.quantityBase)), row.lot.quantityAvailableBase) <= 0)
}
export function validReceipt(t: Transfer, rows: readonly ReceiptSelection[]) {
  return t.status === 'in_transit' && t.allocations.length > 0 && rows.length === t.allocations.length && new Set(rows.map(row => row.allocationId.toLowerCase())).size === rows.length && rows.every(row => {
    const allocation = t.allocations.find(allocation => equalId(row.allocationId, allocation.id))
    return allocation && fits(row.quantityBase, maximumQuantity, 12, true) && compare(row.quantityBase, allocation.dispatchedQuantityBase) <= 0
  })
}
export const actionLabel: Record<GoodsAction, string> = { create: 'solicitud', approve: 'aprobación', cancel: 'cancelación', dispatch: 'despacho', receive: 'recepción' }
