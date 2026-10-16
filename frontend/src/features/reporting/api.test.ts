import { expect, it } from 'vitest'
import { HttpClient } from '../../shared/api/http'
import { reportsApi } from './api'
import { initialFilters } from './filters'
import { reportView } from './view'
import { reportingServer, reports, workspace, id, json } from './reporting.test.support'
import type { ReportKind } from './contracts'

it.each(Object.keys(reports) as ReportKind[])('decodes the real %s contract using exact decimals and scoped paths', async kind => {
  const fake = reportingServer(); const f = { ...initialFilters(workspace.branchId), kind }; const result = await reportsApi(new HttpClient(fake.fetcher)).read(workspace, f)
  expect(result.kind).toBe(kind); expect(result.data.tenantId).toBe(workspace.tenantId); expect(fake.fetcher.mock.calls[0]?.[0]).toContain(`/api/tenants/${workspace.tenantId}/reports/${kind}?`)
  expect(reportView(result, () => 'Nombre consultado').explanation.length).toBeGreaterThan(10); expect(fake.writes()).toHaveLength(0)
  if (result.kind === 'overview') { expect(result.data.consolidatedTotals.netSalesAmount.literal).toBe('9007199254740993.1234'); expect(result.data.consolidatedTotals.confirmedSaleCount.literal).toBe('9007199254740993') }
})
it('rejects foreign scope, mismatched pages and malformed numeric data instead of fabricating empty metrics', async () => {
  const fake = reportingServer(); const api = reportsApi(new HttpClient(fake.fetcher)); const f = initialFilters(workspace.branchId)
  fake.fetcher.mockResolvedValueOnce(json({ ...reports.overview, tenantId: id(99) })); await expect(api.read(workspace, f)).rejects.toThrow()
  fake.fetcher.mockResolvedValueOnce(json({ ...reports.dashboard, branchId: id(99) })); await expect(api.read(workspace, { ...f, kind: 'dashboard' })).rejects.toThrow()
  fake.fetcher.mockResolvedValueOnce(json({ ...reports.sales, offset: 20 })); await expect(api.read(workspace, { ...f, kind: 'sales' })).rejects.toThrow()
  fake.fetcher.mockResolvedValueOnce(json({ ...reports.overview, consolidatedTotals: { ...reports.overview.consolidatedTotals, netSalesAmount: '1.00' } })); await expect(api.read(workspace, f)).rejects.toThrow()
})
it('keeps report totals distinct from page rows, bases and compensation dates', async () => {
  const api = reportsApi(new HttpClient(reportingServer().fetcher)); const f = initialFilters(workspace.branchId)
  const sales = reportView(await api.read(workspace, { ...f, kind: 'sales' }), () => 'Sucursal')
  expect(sales.explanation).toContain('no esta página'); expect(sales.explanation).toContain('cantidades de productos diferentes')
  const commission = reportView(await api.read(workspace, { ...f, kind: 'commissions' }), () => 'Sucursal')
  expect(commission.explanation).toContain('fuera del periodo'); expect(commission.rows[0]?.cells).toContain('2026-11-01T12:00:00Z')
})
