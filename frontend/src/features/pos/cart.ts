import type { ProductUnit } from '../catalog'
import { JsonNumber } from '../../shared/api/exactJson'
import { add, compare, decimalInput, fits, maximumMoney, maximumQuantity, multiply, roundEven4, zero } from '../../shared/api/decimal'
import type { ProductSelection } from './productSelection'
import type { SaleLineInput, SaleState } from './sales.api'

export interface CartLine {
  productId: string; name: string; unitId: string; units: ProductUnit[]; quantity: string
  priceKind: 'retail' | 'wholesale'; basePrice: JsonNumber; stock: JsonNumber | null
}
export const lineKey = (line: CartLine) => `${line.productId}/${line.unitId}/${line.priceKind}`
export const quantityValue = (line: CartLine) => decimalInput(line.quantity, maximumQuantity, 12)
export function lineAmounts(line: CartLine) {
  const quantity = quantityValue(line); const unit = line.units.find(item => item.id === line.unitId && item.isActive)
  if (!quantity || !unit) return null
  const base = multiply(quantity, unit.conversionToBase)
  const unitPrice = multiply(line.basePrice, unit.conversionToBase)
  const total = roundEven4(multiply(base, line.basePrice))
  if (!fits(base, maximumQuantity, 12) || !fits(total, maximumMoney, 4, true)) return null
  return { quantity, base, unitPrice, total }
}
export function cartError(lines: CartLine[]): string | null {
  if (lines.length > 200) return 'El borrador admite hasta 200 líneas.'
  if (new Set(lines.map(lineKey)).size !== lines.length) return 'Esta presentación ya está en el carrito. Ajusta su cantidad.'
  const used = new Map<string, JsonNumber>(); const stock = new Map<string, JsonNumber>()
  let total = zero
  for (const line of lines) {
    const amounts = lineAmounts(line)
    if (!amounts) return 'Revisa cantidades, presentaciones e importes: deben ser positivos y representables exactamente.'
    used.set(line.productId, add(used.get(line.productId) ?? zero, amounts.base))
    if (line.stock && (!stock.has(line.productId) || compare(line.stock, stock.get(line.productId)!) < 0)) stock.set(line.productId, line.stock)
    total = add(total, amounts.total)
  }
  for (const [id, available] of stock) if (compare(used.get(id)!, available) > 0) return 'La cantidad acumulada de un producto supera el stock consultado en unidades base.'
  return fits(total, maximumMoney, 4, true) ? null : 'El total supera el importe admitido.'
}
export function addSelection(lines: CartLine[], selection: ProductSelection): CartLine[] {
  const { product, unit } = selection
  const next = lines.map(line => line.productId === product.businessProductId ? { ...line, units: product.units,
    stock: product.quantityAvailableBase, basePrice: line.priceKind === 'retail' ? product.retailPrice : line.basePrice } : line)
  const existing = next.find(line => line.productId === product.businessProductId && line.unitId === unit.id && line.priceKind === 'retail')
  const quantity = selection.quantity ?? new JsonNumber('1')
  if (existing) {
    const previous = quantityValue(existing)
    if (!previous) throw new Error('Corrige la cantidad de esta línea antes de agregar más unidades.')
    existing.quantity = add(previous, quantity).literal
  } else next.push({ productId: product.businessProductId, name: product.name, unitId: unit.id, units: product.units,
    quantity: quantity.literal, priceKind: 'retail', basePrice: product.retailPrice, stock: product.quantityAvailableBase })
  const error = cartError(next)
  if (error) throw new Error(error)
  return next
}
export const estimateTotal = (lines: CartLine[]) => lines.reduce((total, line) => add(total, lineAmounts(line)?.total ?? zero), zero)
export function saleInputs(lines: CartLine[]): SaleLineInput[] {
  if (cartError(lines)) throw new Error('Invalid cart')
  return lines.map(line => ({ businessProductId: line.productId, productUnitId: line.unitId, quantity: quantityValue(line)!, priceKind: line.priceKind }))
}
export function restoreCart(sale: SaleState): CartLine[] {
  return sale.lines.map(line => ({ productId: line.businessProductId, name: line.productNameSnapshot,
    unitId: line.productUnitIdSnapshot, quantity: line.quantity.literal, basePrice: line.unitPriceSnapshot, priceKind: line.priceKind, stock: null,
    units: [{ id: line.productUnitIdSnapshot, name: line.unitNameSnapshot, conversionToBase: line.conversionToBaseSnapshot, isActive: true, isBaseUnit: compare(line.conversionToBaseSnapshot, new JsonNumber('1')) === 0 }] }))
}
export function matchesSaved(lines: CartLine[], sale: SaleState): boolean {
  if (cartError(lines) || lines.length !== sale.lines.length) return false
  return lines.every(line => sale.lines.some(saved => saved.businessProductId === line.productId && saved.productUnitIdSnapshot === line.unitId &&
    saved.priceKind === line.priceKind && compare(saved.quantity, quantityValue(line)!) === 0))
}
