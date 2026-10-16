import { dateOnly } from '../../shared/api/contracts'
import { add, compare, isSystemDecimal, multiply, systemDecimalInput, zero } from '../../shared/api/decimal'
import type { JsonNumber } from '../../shared/api/exactJson'
import type { ProductDetail, ProductUnit } from '../catalog'
import type { PurchaseLineInput } from './api'

export interface ReceiptLine { key: string; name: string; unitName: string; baseQuantity: JsonNumber; input: PurchaseLineInput }
export function prepareLine(detail: ProductDetail, unit: ProductUnit | undefined, quantityText: string, costText: string, batchText: string, expirationText: string): ReceiptLine | null {
  const quantity = systemDecimalInput(quantityText); const cost = systemDecimalInput(costText, true)
  if (!quantity || !cost || !unit || !detail.product.isActive || !unit.isActive || !detail.units.some(row => row.id === unit.id)) return null
  const baseQuantity = multiply(quantity, unit.conversionToBase)
  if (!isSystemDecimal(baseQuantity) || compare(baseQuantity, zero) <= 0 || batchText.trim().length > 128) return null
  let expirationDate: string | null = null
  if (expirationText) { try { expirationDate = dateOnly(expirationText) } catch { return null } }
  if (detail.product.productType === 'medicine' && (!batchText.trim() || !expirationDate)) return null
  return { key: crypto.randomUUID(), name: detail.product.name, unitName: unit.name, baseQuantity,
    input: { businessProductId: detail.product.id, productUnitId: unit.id, quantity, unitCost: cost, batchNumber: batchText.trim() || null, expirationDate } }
}
// Documentary estimate, not a new monetary rounding rule or a backend purchase total.
export const receiptTotal = (lines: { quantity: JsonNumber; unitCost: JsonNumber }[]) => lines.reduce((sum, row) => add(sum, multiply(row.quantity, row.unitCost)), zero)
