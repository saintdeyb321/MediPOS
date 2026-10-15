import { ApiError } from './http'
import { JsonNumber } from './exactJson'

export type Decoder<T> = (value: unknown) => T
function invalid(): never { throw new ApiError(0, 'client.unexpected_response') }
export const text: Decoder<string> = value => typeof value === 'string' ? value : invalid()
export const boolean: Decoder<boolean> = value => typeof value === 'boolean' ? value : invalid()
export const uuid: Decoder<string> = value => typeof value === 'string' &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) &&
  value !== '00000000-0000-0000-0000-000000000000' ? value : invalid()
export const utc: Decoder<string> = value => /(?:Z|\+00:00)$/.test(text(value)) && Number.isFinite(Date.parse(text(value))) ? text(value) : invalid()
export const decimal: Decoder<JsonNumber> = value => value instanceof JsonNumber ? value : invalid()
export const uint: Decoder<number> = value => {
  const literal = decimal(value).literal
  return /^\d+$/.test(literal) && BigInt(literal) <= 4294967295n ? Number(literal) : invalid()
}
export const nullable = <T,>(decode: Decoder<T>): Decoder<T | null> => value => value === null ? null : decode(value)
export const array = <T,>(decode: Decoder<T>): Decoder<T[]> => value => Array.isArray(value) ? value.map(decode) : invalid()
export const oneOf = <T extends string>(...values: T[]): Decoder<T> => value => values.find(item => item === value) ?? invalid()
export function object<T>(shape: { [K in keyof T]: Decoder<T[K]> }): Decoder<T> {
  return value => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid()
    const input = value as Record<string, unknown>
    const result = {} as T
    for (const key of Object.keys(shape) as (keyof T & string)[]) result[key] = shape[key](input[key])
    return result
  }
}

export interface BranchScope { tenantId: string; branchId: string }
export function requireScope(actual: BranchScope, expected: BranchScope): void {
  if (uuid(actual.tenantId).toLowerCase() !== uuid(expected.tenantId).toLowerCase() ||
      uuid(actual.branchId).toLowerCase() !== uuid(expected.branchId).toLowerCase()) invalid()
}
export function branchPath(scope: BranchScope): string {
  return `/api/tenants/${uuid(scope.tenantId)}/branches/${uuid(scope.branchId)}`
}
export const scopedKey = (scope: BranchScope, resource: string, ...parameters: readonly unknown[]) =>
  ['tenant', uuid(scope.tenantId), uuid(scope.branchId), resource, ...parameters] as const
