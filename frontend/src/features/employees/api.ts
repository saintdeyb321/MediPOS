import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, nullable, object, oneOf, text, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import type { TenantRole } from '../access'
import { timeInput, validSchedule, weekday, wireTime } from './schedule'
import type { WorkWindow } from './schedule'

// EmployeeEndpoints / EmployeeDirectoryQueries / MembershipDetails; no invitation or global user directory.
export interface Employee { membershipId: string; userId: string; displayName: string; role: TenantRole; isActive: boolean; createdAt: string; branchIds: string[]; schedule: WorkWindow[] }
export interface Membership { id: string; tenantId: string; userId: string; role: TenantRole; isActive: boolean; createdAt: string; deactivatedAt: string | null }
export interface CreateEmployeeInput { userId: string; role: TenantRole }
const role = oneOf('owner', 'pharmacist', 'cashier')
const window = object<WorkWindow>({ dayOfWeek: weekday, startTime: wireTime, endTime: wireTime })
const summary = object<Employee>({ membershipId: uuid, userId: uuid, displayName: text, role, isActive: boolean, createdAt: utc, branchIds: array(uuid), schedule: array(window) })
const membership = object<Membership>({ id: uuid, tenantId: uuid, userId: uuid, role, isActive: boolean, createdAt: utc, deactivatedAt: nullable(utc) })
export const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
export function canDeactivate(employee: Employee) { return employee.isActive && employee.role !== 'owner' }
function uniqueIds(ids: readonly string[]) { const normalized = ids.map(id => uuid(id).toLowerCase()); return new Set(normalized).size === normalized.length }
function scoped(data: unknown, scope: BranchScope, target?: Employee) {
  const result = membership(data)
  if (!sameId(result.tenantId, uuid(scope.tenantId)) || target && (!sameId(result.id, target.membershipId) || !sameId(result.userId, target.userId) || result.role !== target.role)) throw new ApiError(0, 'client.unexpected_response')
  return result
}
export function employeesApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}/employees`
  return {
    async list(scope: BranchScope, offset = 0, limit = 20, signal?: AbortSignal) {
      if (!Number.isInteger(offset) || offset < 0 || offset > 10000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid employee page')
      const result = array(summary)(await http.request(`${root(scope)}?offset=${offset}&limit=${limit}`, { signal }))
      if (!uniqueIds(result.map(row => row.membershipId)) || result.some(row => !uniqueIds(row.branchIds) || !validSchedule(row.schedule))) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async create(scope: BranchScope, input: CreateEmployeeInput, signal?: AbortSignal) {
      // Transport ready; UI creation stays blocked until an official identity-association flow exists.
      const data = await http.request(root(scope), { method: 'POST', body: { userId: uuid(input.userId), role: role(input.role) }, signal })
      try { const result = scoped(data, scope); if (!sameId(result.userId, input.userId) || result.role !== input.role || !result.isActive) throw new Error(); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
    async branches(scope: BranchScope, employee: Employee, branchIds: string[], availableIds: readonly string[], signal?: AbortSignal) {
      if (!employee.isActive || !uniqueIds(branchIds) || branchIds.some(id => !availableIds.some(available => sameId(id, available)))) throw new ApiError(400, 'employees.invalid_branches')
      const data = await http.request(`${root(scope)}/${uuid(employee.membershipId)}/branches`, { method: 'PUT', body: { branchIds }, signal })
      if (data !== undefined) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
    },
    async schedule(scope: BranchScope, employee: Employee, windows: WorkWindow[], signal?: AbortSignal) {
      if (!employee.isActive || !validSchedule(windows)) throw new ApiError(400, 'employees.invalid_schedule')
      const data = await http.request(`${root(scope)}/${uuid(employee.membershipId)}/schedule`, { method: 'PUT', body: { windows: windows.map(row => ({ dayOfWeek: row.dayOfWeek, startTime: timeInput(row.startTime)!, endTime: timeInput(row.endTime)! })) }, signal })
      if (data !== undefined) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
    },
    async deactivate(scope: BranchScope, employee: Employee, signal?: AbortSignal) {
      // No count from a paginated snapshot can make Owner deactivation safe without a server invariant.
      if (!canDeactivate(employee)) throw new ApiError(409, 'employees.owner_deactivation_blocked')
      const data = await http.request(`${root(scope)}/${uuid(employee.membershipId)}/deactivate`, { method: 'POST', body: {}, signal })
      try { const result = scoped(data, scope, employee); if (result.isActive || !result.deactivatedAt) throw new Error(); return result }
      catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
    },
  }
}
