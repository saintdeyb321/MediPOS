import { JsonNumber } from '../../shared/api/exactJson'

// SyncPayload v1: ordinal UTF-16 keys, exact coefficient/exponent, uppercase Unicode escapes.
export function canonicalJson(value: unknown): string {
  function quoted(value: string): string {
    if (value.includes('\0') || /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/.test(value)) throw new TypeError('Invalid sync Unicode')
    let result = '"'
    for (let i = 0; i < value.length; i++) {
      const c = value[i]!; const code = value.charCodeAt(i)
      result += c === '"' || c === '\\' ? '\\' + c : code < 32 || code > 126 ? '\\u' + code.toString(16).toUpperCase().padStart(4, '0') : c
    }
    return result + '"'
  }
  function number(literal: string) {
    const negative = literal.startsWith('-'); const [mantissa, power = '0'] = literal.replace(/^-/, '').toLowerCase().split('e')
    let exponent = Number(power); if (!Number.isInteger(exponent) || Math.abs(exponent) > 1000) throw new RangeError('Invalid sync number')
    exponent -= mantissa!.split('.')[1]?.length ?? 0
    const digits = mantissa!.replace('.', '').replace(/^0+/, '')
    if (!digits) return '0'
    const coefficient = digits.replace(/0+$/, ''); exponent += digits.length - coefficient.length
    if (Math.abs(exponent) > 1000) throw new RangeError('Invalid sync number')
    return (negative ? '-' : '') + coefficient + 'e' + exponent
  }
  function write(value: unknown, depth: number): string {
    if (depth > 16) throw new RangeError('Sync depth exceeded')
    if (value instanceof JsonNumber) return number(value.literal)
    if (typeof value === 'number' && Number.isSafeInteger(value)) return number(String(value))
    if (value === null || typeof value === 'boolean') return String(value)
    if (typeof value === 'string') return quoted(value)
    if (Array.isArray(value)) return '[' + value.map(v => write(v, depth + 1)).join(',') + ']'
    if (!value || typeof value !== 'object' || Object.getPrototypeOf(value) !== Object.prototype) throw new TypeError('Invalid sync document')
    return '{' + Object.keys(value).sort().map(key => quoted(key) + ':' + write((value as Record<string, unknown>)[key], depth + 1)).join(',') + '}'
  }
  const result = write(value, 0)
  if (!result.startsWith('{') || result === '{}' || new TextEncoder().encode(result).length > 60 * 1024) throw new RangeError('Sync event too large')
  return result
}
export async function payloadHash(payload: string) {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(payload))
  return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('')
}
