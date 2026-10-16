import { expect, it } from 'vitest'
import { goodsPermissions, goodsSettled, requestLine, validRequest, validDispatch, validReceipt, difference } from './model'
import { allowed, detail, id, lot, n, transfer, workspace } from './transfers.test.support'
import type { Transfer } from './api'

it('converts units exactly at numeric(28,12) bounds without rounding, allowing active zero-stock products', () => {
  const unit = { ...detail.units[1]!, conversionToBase: n('0.000000000001') }
  expect(requestLine(detail, unit, '1')?.base.literal).toBe('0.000000000001')
  expect(requestLine(detail, detail.units[0], '9999999999999999.999999999999')?.base.literal).toBe('9999999999999999.999999999999')
  for (const value of ['0', '-1', '1e2', '0.0000000000001', '10000000000000000']) expect(requestLine(detail, detail.units[0], value)).toBeNull()
  expect(requestLine(detail, unit, '0.1')).toBeNull()
  expect(requestLine(detail, detail.units[1], '9999999999999999')).toBeNull()
  expect(requestLine({ ...detail, product: { ...detail.product, isActive: false } }, detail.units[0], '1')).toBeNull()
  expect(requestLine(detail, { ...unit, isActive: false }, '1')).toBeNull()
})
it('rejects duplicate product/unit pairs and more than 200 request lines, but permits distinct units', () => {
  const line = requestLine(detail, detail.units[0], '1')!; const box = requestLine(detail, detail.units[1], '1')!
  expect(validRequest([])).toBe(false); expect(validRequest([line, line])).toBe(false); expect(validRequest([line, box])).toBe(true)
  const lines = Array.from({ length: 200 }, (_, index) => ({ ...line, input: { ...line.input, businessProductId: id(500 + index) } }))
  expect(validRequest(lines)).toBe(true); expect(validRequest([...lines, box])).toBe(false)
})
it.each(['owner', 'pharmacist', 'cashier'] as const)('requires actual access at each endpoint for %s', role => {
  const w = { ...workspace, access: { ...workspace.access, role } }; const src = allowed(transfer.sourceBranchId, role); const dst = allowed(transfer.destinationBranchId, role)
  expect(goodsPermissions(transfer, w).approve).toBe(false)
  expect(goodsPermissions(transfer, w, src, dst).approve).toBe(role !== 'cashier')
  expect(goodsPermissions({ ...transfer, status: 'approved' }, w, src, dst).dispatch).toBe(role !== 'cashier')
  expect(goodsPermissions({ ...transfer, status: 'in_transit' }, w, src, dst).receive).toBe(role !== 'cashier')
  expect(goodsPermissions(transfer, w, undefined, dst).cancel).toBe(role !== 'owner')
  expect(goodsPermissions({ ...transfer, status: 'approved' }, w, undefined, dst).cancel).toBe(false)
  expect(goodsPermissions({ ...transfer, status: 'received' }, w, src, dst)).toEqual({ approve: false, dispatch: false, receive: false, cancel: false })
})
it('rejects denied, foreign tenant/user/membership/branch contexts and non-requester destination cancellation', () => {
  const src = allowed(transfer.sourceBranchId); const dst = allowed(transfer.destinationBranchId, 'cashier')
  for (const changed of [{ userId: id(99) }, { tenantId: id(99) }, { membershipId: id(99) }, { branchId: id(99) }]) expect(goodsPermissions(transfer, workspace, { ...src, context: { ...src.context!, ...changed } }).approve).toBe(false)
  expect(goodsPermissions(transfer, workspace, { ...src, isAllowed: false }).approve).toBe(false)
  expect(goodsPermissions({ ...transfer, events: transfer.events.map(row => ({ ...row, actorId: id(99) })) }, workspace, undefined, dst).cancel).toBe(false)
})
it('requires exact coverage per line across multiple lots and aggregates one lot across distinct presentations', () => {
  const t: Transfer = { ...transfer, status: 'approved', lines: [...transfer.lines, { ...transfer.lines[0]!, id: id(65), productUnitIdSnapshot: detail.units[1]!.id, requestedBaseQuantity: n('10') }] }
  const first = { lot: { ...lot, quantityAvailableBase: n('11') }, input: { transferLineId: id(61), inventoryLotId: lot.inventoryLotId, quantityBase: n('1.000000000001') } }
  const second = { lot: { ...lot, inventoryLotId: id(31) }, input: { transferLineId: id(61), inventoryLotId: id(31), quantityBase: n('0.999999999999') } }
  const box = { ...first, input: { ...first.input, transferLineId: id(65), quantityBase: n('10') } }
  expect(validDispatch(t, [first, second, box])).toBe(false) // 11.000000000001 > 11 of one lot.
  const fit = { ...first, lot: { ...lot, quantityAvailableBase: n('12') } }; const fitBox = { ...box, lot: fit.lot }
  expect(validDispatch(t, [fit, second, fitBox])).toBe(true)
  expect(validDispatch(t, [fit, second])).toBe(false); expect(validDispatch(t, [fit, second, fitBox, fitBox])).toBe(false)
  expect(validDispatch({ ...t, status: 'in_transit' }, [fit, second, fitBox])).toBe(false)
  expect(validDispatch(t, [{ ...fit, lot: { ...fit.lot, businessProductId: id(99) } }, second, fitBox])).toBe(false)
  expect(validDispatch(t, [{ ...fit, input: { ...fit.input, quantityBase: n('0') } }, second, fitBox])).toBe(false)
  expect(validDispatch(t, Array.from({ length: 1001 }, (_, index) => ({ ...fit, input: { ...fit.input, inventoryLotId: id(2000 + index) } })))).toBe(false)
})
it('requires every received allocation once, accepts zero and actual shortages, rejects excess and precision loss', () => {
  const t: Transfer = { ...transfer, status: 'in_transit', allocations: [1, 2].map(value => ({ id: id(80 + value), transferLineId: id(61), businessProductId: detail.product.id, sourceInventoryLotId: lot.inventoryLotId, sourcePurchaseLineId: id(90), batchNumber: lot.batchNumber, expirationDate: lot.expirationDate, dispatchedQuantityBase: n('1'), receivedQuantityBase: null, differenceBase: null })) }
  const rows = [{ allocationId: id(81), quantityBase: n('0') }, { allocationId: id(82), quantityBase: n('0.999999999999') }]
  expect(validReceipt(t, rows)).toBe(true); expect(difference(n('1'), rows[1]!.quantityBase).literal).toBe('0.000000000001')
  expect(validReceipt(t, [rows[0]!])).toBe(false); expect(validReceipt(t, [rows[0]!, rows[0]!])).toBe(false)
  for (const quantityBase of [n('1.000000000001'), n('-1'), n('0.0000000000001')]) expect(validReceipt(t, [rows[0]!, { ...rows[1]!, quantityBase }])).toBe(false)
  expect(validReceipt({ ...t, status: 'received' }, rows)).toBe(false)
  expect(goodsSettled({ ...t, status: 'approved' }, 'dispatch')).toBe(false)
  expect(goodsSettled({ ...t, events: [...t.events, { ...t.events[0]!, eventType: 'dispatched' }] }, 'dispatch')).toBe(true)
})
