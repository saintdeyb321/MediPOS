import { expect, it } from 'vitest'
import { allowedSort, exportBody, initialFilters, moreRows, reportQuery, todayLima, validDates } from './filters'
import { n } from './reporting.test.support'

it('uses Lima calendar boundaries and exact inclusive 31/366-day ranges without UTC date shifting', () => {
  expect(todayLima(new Date('2026-10-17T02:00:00Z'))).toBe('2026-10-16')
  expect(validDates('2026-01-01', '2026-01-31', 31)).toBe(true); expect(validDates('2026-01-01', '2026-02-01', 31)).toBe(false)
  expect(validDates('2024-01-01', '2024-12-31', 366)).toBe(true); expect(validDates('2026-02-30', '2026-03-01', 366)).toBe(false)
  expect(validDates('2026-10-17', '2026-10-16', 31)).toBe(false); expect(validDates('9999-12-31', '9999-12-31', 31)).toBe(false)
})
it('does not send dates with named HTTP periods, or unrelated filters to inventory reports', () => {
  const f = { ...initialFilters(''), kind: 'dashboard' as const, period: 'month' as const, productId: 'ignored', categoryId: 'ignored', employeeId: 'ignored' }
  // UUIDs must be valid even if optional fields are currently irrelevant.
  expect(() => reportQuery(f)).toThrow()
  const query = reportQuery({ ...f, productId: '', categoryId: '', employeeId: '' }); expect(query.toString()).toBe('period=month')
  const custom = reportQuery({ ...f, productId: '', categoryId: '', employeeId: '', period: 'custom' }); expect(custom.has('from')).toBe(true); expect(custom.has('to')).toBe(true)
  expect(reportQuery({ ...initialFilters(''), kind: 'inventory-capital' }).toString()).toBe('')
})
it('matches sort/dimension constraints and does not round huge row counts for pagination', () => {
  expect(allowedSort('product', 'product_name_asc')).toBe(true); expect(allowedSort('category', 'product_name_asc')).toBe(false)
  expect(moreRows(n('9007199254740993'), 10000, 20)).toBe(true); expect(moreRows(n('20'), 0, 20)).toBe(false)
})
it('maps export field names/enums exactly and exports full filters instead of pagination', () => {
  const f = { ...initialFilters(''), kind: 'sales' as const, period: 'custom' as const, offset: 20 }
  expect(exportBody(f, 'xlsx')).toMatchObject({ reportType: 'sales', format: 'xlsx', branchId: null, period: { type: 'custom', fromLocalDate: f.from, toLocalDate: f.to }, dimension: 'branch', sort: 'sales_amount_desc' })
  expect(exportBody(f, 'xlsx')).not.toHaveProperty('offset')
  expect(exportBody({ ...f, kind: 'stock-risk' }, 'pdf')).toMatchObject({ reportType: 'stock_risk', criticalOnly: false }); expect(exportBody({ ...f, kind: 'stock-risk' }, 'pdf')).not.toHaveProperty('period')
  expect(exportBody({ ...f, kind: 'commissions' }, 'pdf')).toHaveProperty('period.type', 'custom')
  expect(() => exportBody({ ...f, kind: 'overview' }, 'pdf')).toThrow()
})
