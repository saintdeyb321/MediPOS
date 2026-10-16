import { ApiError, HttpClient } from '../../shared/api/http'
import { array, boolean, decimal, nullable, object, oneOf, utc, uuid } from '../../shared/api/contracts'
import type { BranchScope } from '../../shared/api/contracts'
import { JsonNumber } from '../../shared/api/exactJson'
import { compare, fits, maximumMoney } from '../../shared/api/decimal'
import { utcTicks } from './instant'

export interface Configuration { isConfigured: boolean; isEnabled: boolean; updatedAt: string | null; updatedByActorId: string | null }
export interface Settings { tenantId: string; isEnabled: boolean; updatedAt: string; updatedByActorId: string }
export interface Rule { id: string; businessProductId: string; ruleType: 'fixed' | 'percentage'; value: JsonNumber; isActive: boolean; validFrom: string; validUntil: string | null; createdAt: string; createdByActorId: string }
export interface RuleInput { ruleType: Rule['ruleType']; value: JsonNumber; validFrom: string; validUntil: string | null }
const configuration = object<Configuration>({ isConfigured: boolean, isEnabled: boolean, updatedAt: nullable(utc), updatedByActorId: nullable(uuid) })
const settings = object<Settings>({ tenantId: uuid, isEnabled: boolean, updatedAt: utc, updatedByActorId: uuid })
const rule = object<Rule>({ id: uuid, businessProductId: uuid, ruleType: oneOf('fixed', 'percentage'), value: decimal, isActive: boolean, validFrom: utc, validUntil: nullable(utc), createdAt: utc, createdByActorId: uuid })
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
export function validRule(input: RuleInput) { const from = utcTicks(input.validFrom); const until = input.validUntil === null ? null : utcTicks(input.validUntil); return from !== null && (input.validUntil === null || until !== null && until > from) && fits(input.value, maximumMoney, 4) && (input.ruleType !== 'percentage' || compare(input.value, new JsonNumber('100')) <= 0) }
export function commissionsApi(http: HttpClient) {
  const root = (scope: BranchScope) => `/api/tenants/${uuid(scope.tenantId)}`
  async function write<T>(scope: BranchScope, path: string, method: 'POST' | 'PUT', body: unknown, decode: (data: unknown) => T, signal?: AbortSignal) {
    const data = await http.request(root(scope) + path, { method, body, signal, exactNumbers: true })
    try { return decode(data) } catch { throw new ApiError(0, 'client.outcome_unknown', undefined, true) }
  }
  return {
    async settings(scope: BranchScope, signal?: AbortSignal) {
      const result = configuration(await http.request(root(scope) + '/commissions/settings', { signal }))
      if (result.isConfigured ? !result.updatedAt || !result.updatedByActorId : result.isEnabled || result.updatedAt !== null || result.updatedByActorId !== null) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    async rules(scope: BranchScope, productId: string | undefined, includeInactive = true, offset = 0, limit = 20, signal?: AbortSignal) {
      if (!Number.isInteger(offset) || offset < 0 || offset > 100000 || !Number.isInteger(limit) || limit < 1 || limit > 100) throw new RangeError('Invalid rule page')
      const query = new URLSearchParams({ includeInactive: String(includeInactive), offset: String(offset), limit: String(limit) }); if (productId) query.set('productId', uuid(productId))
      const result = array(rule)(await http.request(`${root(scope)}/commissions/rules?${query}`, { signal, exactNumbers: true }))
      if (result.length > limit || new Set(result.map(row => row.id.toLowerCase())).size !== result.length || result.some(row => productId && !same(row.businessProductId, productId) || !includeInactive && !row.isActive || !validRule(row))) throw new ApiError(0, 'client.unexpected_response')
      return result
    },
    enable(scope: BranchScope, isEnabled: boolean, signal?: AbortSignal) { return write(scope, '/commissions/settings', 'PUT', { isEnabled }, data => { const result = settings(data); if (!same(result.tenantId, scope.tenantId) || result.isEnabled !== isEnabled) throw new Error('scope'); return result }, signal) },
    setRule(scope: BranchScope, productId: string, input: RuleInput, signal?: AbortSignal) {
      if (!validRule(input)) throw new ApiError(400, 'commissions.invalid_rule')
      return write(scope, `/products/${uuid(productId)}/commission-rule`, 'PUT', input, data => { const result = rule(data); if (!same(result.businessProductId, productId) || result.ruleType !== input.ruleType || compare(result.value, input.value) !== 0 || utcTicks(result.validFrom) !== utcTicks(input.validFrom) || (result.validUntil === null ? null : utcTicks(result.validUntil)) !== (input.validUntil === null ? null : utcTicks(input.validUntil)) || !result.isActive) throw new Error('scope'); return result }, signal)
    },
    deactivate(scope: BranchScope, current: Rule, signal?: AbortSignal) { return write(scope, `/commissions/rules/${uuid(current.id)}/deactivate`, 'POST', {}, data => { const result = rule(data); if (!same(result.id, current.id) || !same(result.businessProductId, current.businessProductId) || result.isActive) throw new Error('scope'); return result }, signal) },
  }
}
