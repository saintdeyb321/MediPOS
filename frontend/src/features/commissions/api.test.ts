import { expect, it } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { commissionsApi, validRule } from './api'
import { utcTicks } from './instant'
import { commissionServer, detail, id, n, rule, workspace } from './commissions.test.support'

it('reads unconfigured settings without a write and validates exact numeric rule boundaries and UTC ticks', async () => {
  const fake = commissionServer(); const api = commissionsApi(new HttpClient(fake.fetcher)); expect(await api.settings(workspace)).toMatchObject({ isConfigured: false, isEnabled: false }); expect(fake.writes()).toHaveLength(0)
  expect(validRule({ ...rule, value: n('99999999999999.9999') })).toBe(true)
  for (const value of ['0', '0.00001', '100000000000000']) expect(validRule({ ...rule, value: n(value) })).toBe(false)
  expect(validRule({ ...rule, ruleType: 'percentage', value: n('100') })).toBe(true); expect(validRule({ ...rule, ruleType: 'percentage', value: n('100.0001') })).toBe(false)
  expect(validRule({ ...rule, validFrom: '2026-10-16T00:00:00.0000001Z', validUntil: '2026-10-16T00:00:00.0000002Z' })).toBe(true)
  expect(utcTicks('2026-10-16T00:00:00.0000001Z')).toBe(utcTicks('2026-10-16T00:00:00.0000001+00:00'))
  for (const date of ['2026-02-30T00:00:00Z', '2026-10-16T00:00:00-05:00', '2026-10-16', '2026-10-16T24:00:00Z']) expect(utcTicks(date)).toBeNull()
})
it('uses official settings/rule/deactivation routes with CSRF and rereadable history', async () => {
  const fake = commissionServer(); fake.commissionState.rules = [rule]; const api = commissionsApi(new HttpClient(fake.fetcher))
  await api.enable(workspace, true); const saved = await api.setRule(workspace, detail.product.id, { ...rule, value: n('1.0001') }); await api.deactivate(workspace, saved)
  expect(String(fake.writes()[0]?.[0])).toContain('/commissions/settings'); expect(fake.writes()[1]?.[1]?.body).toContain('"value":1.0001'); expect(String(fake.writes()[2]?.[0])).toContain(`/commissions/rules/${id(601)}/deactivate`)
  expect(new Headers(fake.writes()[1]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('transfers-unit-csrf')
  const rows = await api.rules(workspace, detail.product.id, true); expect(rows).toHaveLength(2); expect(rows.every(row => !row.isActive)).toBe(true)
})
