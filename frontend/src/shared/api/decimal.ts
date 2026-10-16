import { JsonNumber } from './exactJson'

// Exact coefficients only. Number is used for bounded scales, never monetary values.
function parts(value: JsonNumber) {
  const [mantissa, exponentText = '0'] = value.literal.toLowerCase().split('e')
  const exponent = Number(exponentText)
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 28) throw new RangeError('Unsupported decimal scale')
  const [whole, fraction = ''] = mantissa!.split('.')
  return { coefficient: BigInt(whole! + fraction), scale: fraction.length - exponent }
}
function value(coefficient: bigint, scale: number): JsonNumber {
  const sign = coefficient < 0n ? '-' : ''
  const digits = (coefficient < 0n ? -coefficient : coefficient).toString()
  if (scale <= 0) return new JsonNumber(sign + digits + '0'.repeat(-scale))
  const padded = digits.padStart(scale + 1, '0')
  return new JsonNumber(sign + padded.slice(0, -scale) + '.' + padded.slice(-scale))
}
export function decimalLabel(input: JsonNumber, minimumFractionDigits = 0): string {
  const { coefficient, scale } = parts(input)
  const literal = value(coefficient, scale).literal
  const [whole, fraction = ''] = literal.split('.')
  const padded = fraction.replace(/0+$/, '').padEnd(minimumFractionDigits, '0')
  return whole + (padded ? '.' + padded : '')
}
export function compare(a: JsonNumber, b: JsonNumber): number {
  const left = parts(a); const right = parts(b); const scale = Math.max(left.scale, right.scale)
  const delta = left.coefficient * 10n ** BigInt(scale - left.scale) - right.coefficient * 10n ** BigInt(scale - right.scale)
  return delta < 0n ? -1 : delta > 0n ? 1 : 0
}
export function add(a: JsonNumber, b: JsonNumber): JsonNumber {
  const left = parts(a); const right = parts(b); const scale = Math.max(left.scale, right.scale)
  return value(left.coefficient * 10n ** BigInt(scale - left.scale) + right.coefficient * 10n ** BigInt(scale - right.scale), scale)
}
export function multiply(a: JsonNumber, b: JsonNumber): JsonNumber {
  const left = parts(a); const right = parts(b)
  return value(left.coefficient * right.coefficient, left.scale + right.scale)
}
export function roundEven4(input: JsonNumber): JsonNumber {
  const { coefficient, scale } = parts(input)
  if (scale <= 4) return value(coefficient, scale)
  const sign = coefficient < 0n ? -1n : 1n
  const magnitude = coefficient * sign; const divisor = 10n ** BigInt(scale - 4)
  const quotient = magnitude / divisor; const remainder = magnitude % divisor
  const increment = remainder * 2n > divisor || remainder * 2n === divisor && quotient % 2n !== 0n
  return value(sign * (quotient + (increment ? 1n : 0n)), 4)
}
export const zero = new JsonNumber('0')
export const maximumMoney = new JsonNumber('99999999999999.9999')
export const maximumQuantity = new JsonNumber('9999999999999999.999999999999')
export function fits(input: JsonNumber, maximum: JsonNumber, scale: number, allowZero = false): boolean {
  const parsed = parts(input)
  return compare(input, zero) >= (allowZero ? 0 : 1) && compare(input, maximum) <= 0 &&
    (parsed.scale <= scale || parsed.coefficient % 10n ** BigInt(parsed.scale - scale) === 0n)
}
export function decimalInput(text: string, maximum: JsonNumber, scale: number, allowZero = false): JsonNumber | null {
  if (!/^(?:0|[1-9]\d*)(?:[.,]\d+)?$/.test(text.trim()) || text.length > 60) return null
  const parsed = new JsonNumber(text.trim().replace(',', '.'))
  return fits(parsed, maximum, scale, allowZero) ? parsed : null
}
