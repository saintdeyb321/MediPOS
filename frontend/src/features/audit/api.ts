import { array, boolean, object, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { ApiError, HttpClient } from '../../shared/api/http'

const entry = object({ auditId: uuid, actorId: uuid, action: text, entityType: text, entityId: uuid, occurredAt: utc, correlationId: text, hasBefore: boolean, hasAfter: boolean })
export interface AuditFilters { fromUtc: string; toUtc: string; entityId: string; offset: number }
export function auditApi(http: HttpClient) {
  return { async list(w: BranchScope, f: AuditFilters, signal?: AbortSignal) {
    if (!validPeriod(f) || !Number.isInteger(f.offset) || f.offset < 0 || f.offset > 10000) throw new RangeError('Invalid audit filters')
    const query = new URLSearchParams({ fromUtc: f.fromUtc, toUtc: f.toUtc, offset: String(f.offset), limit: '20' }); if (f.entityId) query.set('entityId', uuid(f.entityId))
    const rows = array(entry)(await http.request(`/api/tenants/${uuid(w.tenantId)}/audit?${query}`, { signal, exactNumbers: true }))
    if (rows.length > 20 || rows.some(row => f.entityId && row.entityId !== f.entityId)) throw new ApiError(0, 'client.unexpected_response')
    return rows
  } }
}
export function validPeriod(f: AuditFilters) { try { utc(f.fromUtc); utc(f.toUtc); if (f.entityId) uuid(f.entityId); const span = Date.parse(f.toUtc) - Date.parse(f.fromUtc); return span > 0 && span <= 31 * 86400000 } catch { return false } }
