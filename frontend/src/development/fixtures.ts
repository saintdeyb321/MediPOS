// Imported only from an import.meta.env.DEV branch. Never a production auth fallback.
import { ApiError } from '../services/http'
import type { MediPosService, TenantRole } from '../types/contracts'
import type { Scenario } from '../types/development'
const userId = '019a0162-0000-7000-8000-000000000001'
const tenantId = '019a0162-0000-7000-8000-000000000002'
const membershipId = '019a0162-0000-7000-8000-000000000003'
const legalEntityId = '019a0162-0000-7000-8000-000000000004'
const branches = [
  { id: '019a0162-0000-7000-8000-000000000005', tenantId, legalEntityId, name: 'Sucursal principal', isMainHub: false, createdAt: '2026-10-14T12:00:00Z' },
  { id: '019a0162-0000-7000-8000-000000000006', tenantId, legalEntityId, name: 'Sucursal de ejemplo 2', isMainHub: false, createdAt: '2026-10-14T12:00:00Z' },
]
async function wait(signal?: AbortSignal) {
  signal?.throwIfAborted()
  await new Promise(resolve => setTimeout(resolve, 120))
  signal?.throwIfAborted()
}
export function createPreviewService(scenario: Scenario): MediPosService {
  let signedOut = false
  const role: TenantRole = scenario === 'cashier' ? 'cashier' : scenario === 'pharmacist' || scenario === 'scheduleDenied' ? 'pharmacist' : 'owner'
  return {
    async session(signal) { await wait(signal); return signedOut || scenario === 'expired' ? null : { userId } },
    async login() { throw new ApiError(0, 'client.https_required') },
    async logout(signal) { await wait(signal); signedOut = true },
    async tenants(_, signal) {
      await wait(signal)
      if (scenario === 'tenantError') throw new ApiError(503, 'http.503')
      return { items: scenario === 'noTenants' ? [] : [{ tenantId, tradingName: 'Botica de ejemplo', role }], nextAfterTenantId: null }
    },
    async branches(id, signal) {
      await wait(signal)
      if (id !== tenantId) throw new ApiError(403, 'access.membership_missing')
      return scenario === 'noBranches' ? [] : role === 'cashier' ? branches.slice(0, 1) : [...branches]
    },
    async access(id, branchId, signal) {
      await wait(signal)
      const assigned = id === tenantId && branches.some(b => b.id === branchId) && (role !== 'cashier' || branchId === branches[0]?.id)
      const code = !assigned ? 'access.branch_unassigned' : scenario === 'scheduleDenied' ? 'access.schedule_denied' : scenario === 'licenseDenied' ? 'access.license_denied' : 'access.allowed'
      return { isAllowed: code === 'access.allowed', code, context: {
        userId, tenantId: id, branchId, membershipId, role, membershipIsActive: true, branchAssigned: assigned,
        licenseStatus: scenario === 'licenseDenied' ? 'suspended' : 'active', licenseStartsAt: '2026-01-01T00:00:00Z', licenseExpiresAt: '2027-01-01T00:00:00Z',
        withinSchedule: scenario !== 'scheduleDenied', evaluatedAtUtc: new Date().toISOString(),
      } }
    },
  }
}
