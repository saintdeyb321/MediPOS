import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, nullable, object, oneOf, text, uint, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { TenantRole } from '../access'

export type InvitationStatus = 'pending_acceptance' | 'accepted_pending_approval' | 'approved' | 'revoked' | 'expired'
export interface EmployeeInvitation {
  id: string; tenantId: string; clientRequestId: string; role: TenantRole; status: InvitationStatus; version: number
  createdAt: string; expiresAt: string; acceptedAt: string | null; acceptedDisplayName: string | null; acceptedEmail: string | null
  approvedAt: string | null; resultMembershipId: string | null; revokedAt: string | null; revocationReason: string | null; canApprove: boolean
}
const role = oneOf('owner', 'pharmacist', 'cashier')
const invitation = object<EmployeeInvitation>({ id: uuid, tenantId: uuid, clientRequestId: uuid, role, status: oneOf('pending_acceptance', 'accepted_pending_approval', 'approved', 'revoked', 'expired'), version: uint,
  createdAt: utc, expiresAt: utc, acceptedAt: nullable(utc), acceptedDisplayName: nullable(text), acceptedEmail: nullable(text), approvedAt: nullable(utc), resultMembershipId: nullable(uuid),
  revokedAt: nullable(utc), revocationReason: nullable(text), canApprove: boolean })
function scoped(data: unknown, scope: BranchScope, id?: string) {
  return checkScope(invitation(data), scope, id)
}
function checkScope(result: EmployeeInvitation, scope: BranchScope, id?: string) {
  if (result.tenantId.toLowerCase() !== uuid(scope.tenantId).toLowerCase() || id && result.id.toLowerCase() !== id.toLowerCase() || result.version === 0 || result.status === 'approved' && !result.resultMembershipId) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function invitationsApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}/employee-invitations`
  return {
    async list(scope: BranchScope, offset = 0, clientRequestId?: string, signal?: AbortSignal) {
      if (!Number.isInteger(offset) || offset < 0 || offset > 10000) throw new RangeError('Invalid invitation page')
      const query = new URLSearchParams({ offset: String(offset), limit: '20' }); if (clientRequestId) query.set('clientRequestId', uuid(clientRequestId))
      return array(invitation)(await http.request(root(scope) + '?' + query, { signal, exactNumbers: true })).map(row => checkScope(row, scope))
    },
    async read(scope: BranchScope, id: string, signal?: AbortSignal) {
      return scoped(await http.request(root(scope) + '/' + uuid(id), { signal, exactNumbers: true }), scope, id)
    },
    async create(scope: BranchScope, clientRequestId: string, intendedRole: TenantRole, signal?: AbortSignal) {
      const data = await http.request(root(scope), { method: 'POST', body: { clientRequestId: uuid(clientRequestId), role: role(intendedRole) }, signal, exactNumbers: true })
      try {
        const result = object({ invitation, ticket: nullable(text) })(data); checkScope(result.invitation, scope)
        if (result.invitation.clientRequestId.toLowerCase() !== clientRequestId.toLowerCase() || result.invitation.role !== intendedRole || result.ticket !== null && (!result.ticket || result.ticket.length > 4096)) throw new Error('Invalid invitation receipt')
        return result
      } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async approve(scope: BranchScope, value: EmployeeInvitation, signal?: AbortSignal) {
      if (!value.canApprove || value.status !== 'accepted_pending_approval') throw new ApiError(409, 'invitations.unavailable')
      const data = await http.request(root(scope) + '/' + uuid(value.id) + '/approve', { method: 'POST', body: { expectedVersion: value.version, confirmed: true }, signal, exactNumbers: true })
      try { const result = scoped(data, scope, value.id); if (result.status !== 'approved' || result.role !== value.role) throw new Error('Invalid approval receipt'); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async revoke(scope: BranchScope, value: EmployeeInvitation, reason: string, signal?: AbortSignal) {
      if (value.status === 'approved' || value.status === 'revoked' || !reason.trim() || reason.trim().length > 256) throw new ApiError(400, 'request.invalid')
      const data = await http.request(root(scope) + '/' + uuid(value.id) + '/revoke', { method: 'POST', body: { expectedVersion: value.version, reason: reason.trim() }, signal, exactNumbers: true })
      try { const result = scoped(data, scope, value.id); if (result.status !== 'revoked') throw new Error('Invalid revocation receipt'); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async accept(ticket: string, signal?: AbortSignal) {
      if (!ticket || ticket.length > 4096) throw new ApiError(400, 'invitations.unavailable')
      const data = await http.request('/api/employee-invitations/accept', { method: 'POST', body: { ticket }, signal })
      try { return object({ status: oneOf('accepted_pending_approval'), expiresAt: utc })(data) }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
