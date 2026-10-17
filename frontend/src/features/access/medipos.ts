import { ApiError, HttpClient } from '../../shared/api/http'
import { isUuid } from './contracts'
import type { AccessContext, Branch, MediPosService, OperationalAccess, Session, TenantPage, TenantRole } from './contracts'

function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new ApiError(0, 'client.unexpected_response')
  return value as Record<string, unknown>
}
function assert(valid: boolean): asserts valid { if (!valid) throw new ApiError(0, 'client.unexpected_response') }
const role = (value: unknown): value is TenantRole => value === 'owner' || value === 'pharmacist' || value === 'cashier'
const date = (value: unknown) => typeof value === 'string' && /Z$|\+00:00$/.test(value) && Number.isFinite(Date.parse(value))
const optionalDate = (value: unknown) => value === null || date(value)
export function parseSession(data: unknown): Session {
  const value = object(data); assert(isUuid(value.userId))
  const result: Session = { userId: value.userId }
  if (value.expiresAtUtc !== undefined || value.serverTimeUtc !== undefined) {
    assert(optionalDate(value.expiresAtUtc) && date(value.serverTimeUtc))
    result.expiresAtUtc = value.expiresAtUtc as string | null; result.serverTimeUtc = value.serverTimeUtc as string
  }
  return result
}
export function parseTenants(data: unknown): TenantPage {
  const value = object(data); assert(Array.isArray(value.items)); assert(value.nextAfterTenantId === null || isUuid(value.nextAfterTenantId))
  const items = value.items.map(item => {
    const v = object(item); assert(isUuid(v.tenantId) && typeof v.tradingName === 'string' && role(v.role))
    return { tenantId: v.tenantId, tradingName: v.tradingName, role: v.role }
  })
  return { items, nextAfterTenantId: value.nextAfterTenantId }
}
export function parseBranches(data: unknown, tenantId: string): Branch[] {
  assert(Array.isArray(data))
  return data.map(item => {
    const v = object(item)
    assert(isUuid(v.id) && v.tenantId === tenantId && isUuid(v.legalEntityId) && typeof v.name === 'string' && typeof v.isMainHub === 'boolean' && date(v.createdAt))
    return { id: v.id, tenantId, legalEntityId: v.legalEntityId, name: v.name, isMainHub: v.isMainHub, createdAt: v.createdAt as string }
  })
}
export function parseAccess(data: unknown, tenantId: string, branchId: string): OperationalAccess {
  const value = object(data); assert(typeof value.isAllowed === 'boolean' && typeof value.code === 'string')
  let context: AccessContext | null = null
  if (value.context !== null) {
    const v = object(value.context)
    assert(v.tenantId === tenantId && v.branchId === branchId && isUuid(v.userId) && isUuid(v.membershipId) && role(v.role) &&
      typeof v.membershipIsActive === 'boolean' && typeof v.branchAssigned === 'boolean' && typeof v.withinSchedule === 'boolean' &&
      (v.licenseStatus === null || typeof v.licenseStatus === 'string') && optionalDate(v.licenseStartsAt) && optionalDate(v.licenseExpiresAt) && date(v.evaluatedAtUtc))
    context = {
      userId: v.userId, tenantId, branchId, membershipId: v.membershipId, role: v.role,
      membershipIsActive: v.membershipIsActive, branchAssigned: v.branchAssigned, withinSchedule: v.withinSchedule,
      licenseStatus: v.licenseStatus as string | null, licenseStartsAt: v.licenseStartsAt as string | null,
      licenseExpiresAt: v.licenseExpiresAt as string | null, evaluatedAtUtc: v.evaluatedAtUtc as string,
    }
  }
  assert(!value.isAllowed || value.code === 'access.allowed' && context !== null && context.membershipIsActive)
  return { isAllowed: value.isAllowed, code: value.code, context }
}
export function createMediPosService(http: HttpClient): MediPosService {
  return {
    async session(signal) {
      try { return parseSession(await http.request('/auth/me', { signal })) }
      catch (error) { if (error instanceof ApiError && error.status === 401) return null; throw error }
    },
    login: signal => http.login(signal),
    async logout(signal) { await http.request('/auth/logout', { method: 'POST', signal }) },
    async tenants(after, signal) { return parseTenants(await http.request('/api/me/tenants?limit=100' + (after ? `&afterTenantId=${encodeURIComponent(after)}` : ''), { signal })) },
    async branches(tenantId, signal) {
      return parseBranches(await http.request(`/api/tenants/${encodeURIComponent(tenantId)}/branches?limit=100&offset=0`, { signal }), tenantId)
    },
    async access(tenantId, branchId, signal) {
      return parseAccess(await http.request(`/api/tenants/${encodeURIComponent(tenantId)}/access?branchId=${encodeURIComponent(branchId)}`, { signal }), tenantId, branchId)
    },
  }
}
