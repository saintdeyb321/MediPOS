import { parseExactJson } from '../../shared/api/exactJson'
import { transferServer, workspace, id, time, n, json, mountTransfers, detail } from '../transfers/transfers.test.support'
import type { Configuration, Rule, RuleInput } from './api'
export { workspace, id, time, n, json, mountTransfers, detail }
export const rule: Rule = { id: id(600), businessProductId: detail.product.id, ruleType: 'fixed', value: n('0.0001'), isActive: true, validFrom: '2026-10-16T00:00:00Z', validUntil: null, createdAt: time, createdByActorId: workspace.session.userId }
export function commissionServer() {
  const fake = transferServer(); const parent = fake.fetcher.getMockImplementation()!
  const state = { configuration: { isConfigured: false, isEnabled: false, updatedAt: null, updatedByActorId: null } as Configuration, rules: [] as Rule[], reject: 0, lose: '', concurrent: false }
  fake.fetcher.mockImplementation(async (path, init) => {
    const url = new URL(String(path), 'https://unit.test'); const method = init?.method ?? 'GET'
    if (method !== 'GET' && (url.pathname.includes('/commissions/') || url.pathname.endsWith('/commission-rule')) && state.reject) return json({ code: 'commissions.concurrent_change' }, state.reject)
    if (url.pathname.endsWith('/commissions/settings')) {
      if (method === 'GET') return json(state.configuration)
      const body = parseExactJson(String(init?.body)) as { isEnabled: boolean }; state.configuration = { isConfigured: true, isEnabled: body.isEnabled, updatedAt: time, updatedByActorId: workspace.session.userId }
      if (state.lose === 'settings') throw new TypeError('Synthetic settings loss')
      return json({ ...state.configuration, tenantId: workspace.tenantId })
    }
    if (method === 'GET' && url.pathname.endsWith('/commissions/rules')) {
      const product = url.searchParams.get('productId'); const inactive = url.searchParams.get('includeInactive') === 'true'; const offset = Number(url.searchParams.get('offset')); const limit = Number(url.searchParams.get('limit'))
      return json(state.rules.filter(row => (!product || row.businessProductId === product) && (inactive || row.isActive)).slice(offset, offset + limit))
    }
    if (method === 'PUT' && url.pathname.endsWith('/commission-rule')) {
      const body = parseExactJson(String(init?.body)) as RuleInput; const created = { ...rule, ...body, id: id(601), businessProductId: url.pathname.split('/').at(-2)! }
      state.rules = [...state.rules.map(row => ({ ...row, isActive: false })), created]
      if (state.concurrent) state.rules = [{ ...created, isActive: false }, { ...created, id: id(602), value: n('2') }]
      if (state.lose === 'rule') throw new TypeError('Synthetic rule loss')
      return json(created)
    }
    if (method === 'POST' && url.pathname.endsWith('/deactivate')) {
      const target = url.pathname.split('/').at(-2); state.rules = state.rules.map(row => row.id === target ? { ...row, isActive: false } : row)
      if (state.lose === 'deactivate') throw new TypeError('Synthetic deactivation loss')
      return json(state.rules.find(row => row.id === target))
    }
    return parent(path, init)
  })
  return { ...fake, commissionState: state, writes: () => fake.fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
