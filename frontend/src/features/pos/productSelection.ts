import type { ProductMatch, ProductUnit } from '../catalog'
import type { JsonNumber } from '../../shared/api/exactJson'

export interface ProductSelection { product: ProductMatch; unit: ProductUnit }

// Only exact decimal coefficients are compared; prices/quantities never pass through Number.
function parts(value: JsonNumber) {
  const [mantissa, exponentText = '0'] = value.literal.toLowerCase().split('e')
  const exponent = Number(exponentText)
  // The HTTP contract is System.Decimal, whose decimal scale is bounded by 28.
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 28) return null
  const [whole, fraction = ''] = mantissa!.split('.')
  return { coefficient: BigInt(whole! + fraction), scale: fraction.length - exponent }
}

export function decimalLabel(value: JsonNumber, minimumFractionDigits = 0): string {
  const parsed = parts(value)
  if (!parsed) return value.literal
  const negative = parsed.coefficient < 0n
  const digits = (negative ? -parsed.coefficient : parsed.coefficient).toString()
  const scale = Math.max(0, parsed.scale)
  const expanded = parsed.scale < 0 ? digits + '0'.repeat(-parsed.scale) : digits.padStart(scale + 1, '0')
  const whole = scale ? expanded.slice(0, -scale) : expanded
  const fraction = (scale ? expanded.slice(-scale) : '').padEnd(minimumFractionDigits, '0')
  return (negative ? '-' : '') + whole + (fraction ? '.' + fraction : '')
}

export function canSelectUnit(product: ProductMatch, unit: ProductUnit): boolean {
  if (!unit.isActive || !product.units.some(candidate => candidate.id === unit.id)) return false
  const stock = parts(product.quantityAvailableBase)
  const conversion = parts(unit.conversionToBase)
  if (!stock || !conversion || stock.coefficient <= 0n || conversion.coefficient <= 0n) return false
  const scale = Math.max(stock.scale, conversion.scale)
  return stock.coefficient * 10n ** BigInt(scale - stock.scale) >=
    conversion.coefficient * 10n ** BigInt(scale - conversion.scale)
}
