import { array, boolean, nullable, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'

const license = object({ tenantId: uuid, licenseId: uuid, status: oneOf('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'), startsAt: utc, expiresAt: utc, maxBranches: uint })
const summary = object({ tenantId: uuid, tradingName: text, createdAt: utc })
const identity = object({ userId: uuid, displayName: text, email: text })
const details = object({ tenant: summary, license, initialOwnerUserId: nullable(uuid) })
const category = object({ id: uuid, name: text, isActive: boolean, createdAt: utc })
export type PlatformTenant = ReturnType<typeof details>
export type VerifiedIdentity = ReturnType<typeof identity>
export type TrialInput = { tradingName: string; startsAt: string; expiresAt: string; maxBranches: number; status: 'trial' }
export type LicenseAction = 'renew' | 'suspend' | 'reactivate'
export function platformApi(http: HttpClient) {
  const validLicense = (value: unknown) => { const result = license(value); if (result.maxBranches < 1 || result.maxBranches > 5) throw new ApiError(0, 'client.unexpected_response'); return result }
  const mutate = async <T,>(path: string, body: unknown, decode: (value: unknown) => T, signal?: AbortSignal, idempotencyKey?: string) => {
    const value = await http.request(path, { method: 'POST', body, signal, exactNumbers: true, idempotencyKey })
    try { return decode(value) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async categories(signal?: AbortSignal) {
      const result = array(category)(await http.request('/platform/catalog/categories?limit=100', { signal }))
      if (result.length > 100) throw new ApiError(0, 'client.unexpected_response'); return result
    },
    createCategory: (name: string, signal?: AbortSignal) => mutate('/platform/catalog/categories', { name }, category, signal),
    async admission(signal?: AbortSignal) { const result = object({ allowed: boolean })(await http.request('/platform/session', { signal })); if (!result.allowed) throw new ApiError(403, 'access.platform_denied'); return result },
    async tenants(after?: string, signal?: AbortSignal) {
      const result = object({ items: array(summary), nextAfterTenantId: nullable(uuid) })(await http.request('/platform/tenants?limit=20' + (after ? '&afterTenantId=' + uuid(after) : ''), { signal, exactNumbers: true }))
      if (result.items.length > 20) throw new ApiError(0, 'client.unexpected_response'); return result
    },
    async tenant(tenantId: string, signal?: AbortSignal) {
      const result = details(await http.request(`/platform/tenants/${uuid(tenantId)}/details`, { signal, exactNumbers: true }))
      if (result.tenant.tenantId !== tenantId || result.license.tenantId !== tenantId) throw new ApiError(0, 'client.unexpected_response')
      if (result.license.maxBranches < 1 || result.license.maxBranches > 5) throw new ApiError(0, 'client.unexpected_response'); return result
    },
    async identity(userId: string, signal?: AbortSignal) { const result = identity(await http.request(`/platform/identities/${uuid(userId)}`, { signal })); if (result.userId !== userId) throw new ApiError(0, 'client.unexpected_response'); return result },
    create: (input: TrialInput, key: string, signal?: AbortSignal) => mutate('/platform/tenants', input, validLicense, signal, uuid(key)),
    async receipt(key: string, signal?: AbortSignal) { return validLicense(await http.request(`/platform/tenant-requests/${uuid(key)}`, { signal, exactNumbers: true })) },
    bootstrap: (tenantId: string, userId: string, signal?: AbortSignal) => mutate(`/platform/tenants/${uuid(tenantId)}/bootstrap-owner`, { userId: uuid(userId) }, value => {
      const result = object({ membershipId: uuid, tenantId: uuid, userId: uuid, role: oneOf('owner'), isActive: boolean, createdAt: utc })(value)
      if (result.tenantId !== tenantId || result.userId !== userId || !result.isActive) throw new Error('scope'); return result
    }, signal),
    change: (tenant: PlatformTenant, action: LicenseAction, input: { expiresAt: string } | { status: 'trial' | 'active' | 'grace' } | Record<string, never>, signal?: AbortSignal) =>
      mutate(`/platform/tenants/${uuid(tenant.tenant.tenantId)}/licenses/${uuid(tenant.license.licenseId)}/${action}`, input, value => {
        const result = validLicense(value); if (result.tenantId !== tenant.tenant.tenantId || result.licenseId !== tenant.license.licenseId) throw new Error('scope'); return result
      }, signal),
  }
}
