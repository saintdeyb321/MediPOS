import { ApiError, HttpClient } from '../../shared/api/http'
import { uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { reportDecoders } from './contracts'
import type { ReportResult } from './contracts'
import { exportBody, reportQuery } from './filters'
import type { ReportFilters } from './filters'

export function reportsApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}/reports`
  return {
    async read(scope: BranchScope, filters: ReportFilters, signal?: AbortSignal): Promise<ReportResult> {
      const data = reportDecoders[filters.kind](await http.request(`${root(scope)}/${filters.kind}?${reportQuery(filters)}`, { signal, exactNumbers: true }))
      const same = (a: string | null, b: string | null) => a?.toLowerCase() === b?.toLowerCase()
      if (!same(data.tenantId, scope.tenantId) || 'branchId' in data && !same(data.branchId, filters.branchId || null) || 'offset' in data && (data.offset !== filters.offset || data.limit !== 20) || 'rows' in data && data.rows.length > 20) throw new ApiError(0, 'client.unexpected_response')
      if (filters.branchId && (('branches' in data && data.branches.some(row => !same(row.branchId, filters.branchId))) || ('rows' in data && data.rows.some(row => 'branchId' in row && !same(row.branchId, filters.branchId))))) throw new ApiError(0, 'client.unexpected_response')
      if ('period' in data && (data.period.type !== filters.period || data.period.timeZoneId !== 'America/Lima' || filters.period === 'custom' && (data.period.fromLocalDate !== filters.from || data.period.toLocalDate !== filters.to))) throw new ApiError(0, 'client.unexpected_response')
      if ('dimension' in data && (data.dimension !== filters.dimension || data.sort !== filters.sort || !same(data.employeeMembershipId, filters.employeeId || null) || !same(data.businessProductId, filters.productId || null) || !same(data.categoryId, filters.categoryId || null))) throw new ApiError(0, 'client.unexpected_response')
      if ('criticalOnly' in data && (data.criticalOnly !== filters.criticalOnly || !same(data.businessProductId, filters.productId || null) || data.rows.some(row => filters.productId && !same(row.businessProductId, filters.productId) || filters.criticalOnly && !row.isCritical))) throw new ApiError(0, 'client.unexpected_response')
      if ('fromExpirationDate' in data && (data.fromExpirationDate !== filters.from || data.toExpirationDate !== filters.to)) throw new ApiError(0, 'client.unexpected_response')
      if ('mode' in data && data.mode !== filters.rotation) throw new ApiError(0, 'client.unexpected_response')
      if (filters.kind === 'commissions' && 'rows' in data && data.rows.some(row => 'sellerMembershipId' in row && (filters.employeeId && !same(row.sellerMembershipId, filters.employeeId) || filters.productId && !same(row.businessProductId, filters.productId)))) throw new ApiError(0, 'client.unexpected_response')
      return { kind: filters.kind, data } as ReportResult
    },
    async export(scope: BranchScope, filters: ReportFilters, format: 'pdf' | 'xlsx', signal?: AbortSignal) {
      const result = await http.request(`${root(scope)}/exports`, { method: 'POST', body: exportBody(filters, format), signal, responseType: 'blob', timeoutMs: 35_000 })
      const expected = format === 'pdf' ? 'application/pdf' : 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
      if (!(result instanceof Blob) || result.type !== expected || result.size === 0 || result.size > 10 * 1024 * 1024) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
  }
}
