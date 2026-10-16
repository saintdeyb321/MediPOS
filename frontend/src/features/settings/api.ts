import { array, boolean, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'

const legal = object({ id: uuid, tenantId: uuid, legalName: text, ruc: text, createdAt: utc })
const branch = object({ id: uuid, tenantId: uuid, legalEntityId: uuid, name: text, isMainHub: boolean, createdAt: utc })
const license = object({ tenantId: uuid, licenseId: uuid, status: oneOf('trial', 'active', 'grace', 'suspended', 'cancelled', 'purge_pending', 'purged'), startsAt: utc, expiresAt: utc, maxBranches: uint })
export type LegalEntity = ReturnType<typeof legal>
export type BranchDetails = ReturnType<typeof branch>
export type LicenseDetails = ReturnType<typeof license>
export type LegalInput = { legalName: string; ruc: string }
export type BranchInput = { legalEntityId: string; name: string }
export function settingsApi(http: HttpClient) {
  const root = (w: BranchScope) => `/api/tenants/${uuid(w.tenantId)}`
  function scoped<T extends { tenantId: string }>(row: T, w: BranchScope): T { if (row.tenantId !== w.tenantId) throw new ApiError(0, 'client.unexpected_response'); return row }
  function page(offset: number) { if (!Number.isInteger(offset) || offset < 0 || offset > 10000) throw new RangeError('Invalid page'); return `?offset=${offset}&limit=20` }
  return {
    async license(w: BranchScope, signal?: AbortSignal) { const result = scoped(license(await http.request(root(w) + '/license', { signal, exactNumbers: true })), w); if (result.maxBranches < 1 || result.maxBranches > 5) throw new ApiError(0, 'client.unexpected_response'); return result },
    async legal(w: BranchScope, offset: number, signal?: AbortSignal) { const rows = array(legal)(await http.request(root(w) + '/legal-entities' + page(offset), { signal, exactNumbers: true })); if (rows.length > 20) throw new ApiError(0, 'client.unexpected_response'); return rows.map(row => scoped(row, w)) },
    async branches(w: BranchScope, offset: number, signal?: AbortSignal) { const rows = array(branch)(await http.request(root(w) + '/branches' + page(offset), { signal, exactNumbers: true })); if (rows.length > 20) throw new ApiError(0, 'client.unexpected_response'); return rows.map(row => scoped(row, w)) },
    async createLegal(w: BranchScope, input: LegalInput, signal?: AbortSignal) {
      const data = await http.request(root(w) + '/legal-entities', { method: 'POST', body: input, signal, exactNumbers: true })
      try { return scoped(legal(data), w) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async createBranch(w: BranchScope, input: BranchInput, signal?: AbortSignal) {
      const data = await http.request(root(w) + '/branches', { method: 'POST', body: { ...input, legalEntityId: uuid(input.legalEntityId) }, signal, exactNumbers: true })
      try { const result = scoped(branch(data), w); if (result.legalEntityId !== input.legalEntityId) throw new Error('entity'); return result } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
