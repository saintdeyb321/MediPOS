import type { ProductMatch, ProductUnit } from '../catalog'
import type { JsonNumber } from '../../shared/api/exactJson'
import { compare, multiply, zero, fits, maximumQuantity } from '../../shared/api/decimal'
export { decimalLabel } from '../../shared/api/decimal'

export interface ProductSelection { product: ProductMatch; unit: ProductUnit; quantity?: JsonNumber }

// Only exact decimal coefficients are compared; prices/quantities never pass through Number.
export function canSelectUnit(product: ProductMatch, unit: ProductUnit): boolean {
  if (!unit.isActive || !product.units.some(candidate => candidate.id === unit.id)) return false
  try { return compare(unit.conversionToBase, zero) > 0 && compare(product.quantityAvailableBase, unit.conversionToBase) >= 0 }
  catch { return false }
}

export function canSelectQuantity(product: ProductMatch, unit: ProductUnit, quantity: JsonNumber): boolean {
  try {
    const base = multiply(quantity, unit.conversionToBase)
    return unit.isActive && product.units.some(item => item.id === unit.id) && fits(quantity, maximumQuantity, 12) &&
      fits(base, maximumQuantity, 12) && compare(base, product.quantityAvailableBase) <= 0
  } catch { return false }
}
