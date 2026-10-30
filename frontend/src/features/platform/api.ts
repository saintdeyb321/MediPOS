import { array, boolean, nullable, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'

const license = object({ tenantId: uuid, licenseId: uuid, status: oneOf('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'), startsAt: utc, expiresAt: utc, maxBranches: uint })
const administration = object({ licenseStatus: text, expiresAt: utc, maxBranches: uint, ownerStatus: oneOf('active', 'inactive', 'pending', 'claimed', 'revoked', 'unassigned'), ownerEmail: nullable(text), ownerName: nullable(text) })
const summary = object({ tenantId: uuid, tradingName: text, createdAt: utc, administration: nullable(administration) })
const details = object({ tenant: summary, license, initialOwnerUserId: nullable(uuid) })
const category = object({ id: uuid, name: text, isActive: boolean, createdAt: utc })
const ownerInvitation = object({ invitationId: uuid, email: text, status: oneOf('pending', 'claimed', 'revoked'), createdAt: utc, userId: nullable(uuid) })
export type InitialOwner = ReturnType<typeof ownerInvitation>
export type PlatformTenant = ReturnType<typeof details>
export type TrialInput = { tradingName: string; ownerEmail: string; startsAt: string; expiresAt: string; maxBranches: number; status: 'trial' }
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
    createCategory: (name: string, signal?: AbortSignal) => mutate('/platform/catalog/categories', { name }, value => {
      const result = category(value); if (result.name !== name.trim() || !result.isActive) throw new Error('category response mismatch'); return result
    }, signal),
    async admission(signal?: AbortSignal) { const result = object({ allowed: boolean })(await http.request('/platform/session', { signal })); if (!result.allowed) throw new ApiError(403, 'access.platform_denied'); return result },
    async initialOwner(tenantId: string, signal?: AbortSignal) { return object({ invitation: nullable(ownerInvitation) })(await http.request(`/platform/tenants/${uuid(tenantId)}/initial-owner`, { signal })).invitation },
    changeOwner: (tenantId: string, expectedInvitationId: string, email: string | null, signal?: AbortSignal) => mutate(`/platform/tenants/${uuid(tenantId)}/initial-owner/${email === null ? 'revoke' : 'change'}`, { expectedInvitationId: uuid(expectedInvitationId), ...(email === null ? {} : { email }) }, ownerInvitation, signal),
    async tenants(after?: string, signal?: AbortSignal) {
      const result = object({ items: array(summary), nextAfterTenantId: nullable(uuid) })(await http.request('/platform/tenants?limit=20' + (after ? '&afterTenantId=' + uuid(after) : ''), { signal, exactNumbers: true }))
      if (result.items.length > 20) throw new ApiError(0, 'client.unexpected_response'); return result
    },
    async tenant(tenantId: string, signal?: AbortSignal) {
      const result = details(await http.request(`/platform/tenants/${uuid(tenantId)}/details`, { signal, exactNumbers: true }))
      if (result.tenant.tenantId !== tenantId || result.license.tenantId !== tenantId) throw new ApiError(0, 'client.unexpected_response')
      if (result.license.maxBranches < 1 || result.license.maxBranches > 5) throw new ApiError(0, 'client.unexpected_response'); return result
    },
    create: (input: TrialInput, key: string, signal?: AbortSignal) => mutate('/platform/tenants/with-owner', input, validLicense, signal, uuid(key)),
    async receipt(key: string, signal?: AbortSignal) { return validLicense(await http.request(`/platform/tenant-requests/${uuid(key)}`, { signal, exactNumbers: true })) },
    change: (tenant: PlatformTenant, action: LicenseAction, input: { expiresAt: string } | { status: 'trial' | 'active' | 'grace' } | Record<string, never>, signal?: AbortSignal) =>
      mutate(`/platform/tenants/${uuid(tenant.tenant.tenantId)}/licenses/${uuid(tenant.license.licenseId)}/${action}`, input, value => {
        const result = validLicense(value); if (result.tenantId !== tenant.tenant.tenantId || result.licenseId !== tenant.license.licenseId) throw new Error('scope'); return result
      }, signal),
  }
}
