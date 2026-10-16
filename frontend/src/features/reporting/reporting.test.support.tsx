import type { ReportTypes, ReportKind } from './contracts'
import { transferServer, workspace, id, n, time, other, detail, json, mountTransfers } from '../transfers/transfers.test.support'
export { workspace, id, n, time, other, detail, json, mountTransfers }

// Synthetic transport data only. No claim of PostgreSQL aggregation, RLS or real export validation.
export const overviewMetrics = { netSalesAmount: n('9007199254740993.1234'), confirmedSaleCount: n('9007199254740993'), voidedSaleCount: n('0'), openCashSessionCount: n('0'), expiringLotCount: n('0'), expiredLotCount: n('0'), pendingProductTransfersBeforeDispatchInCount: n('0'), inTransitProductTransfersInCount: n('0'), pendingProductTransfersBeforeDispatchOutCount: n('0'), pendingCashTransfersInCount: n('0') }
const base = { tenantId: workspace.tenantId, generatedAtUtc: time }; const scoped = { ...base, branchId: workspace.branchId }; const page = { offset: 0, limit: 20 }
export const period = { type: 'day' as const, fromLocalDate: '2026-10-16', toLocalDate: '2026-10-16', startUtc: time, endExclusiveUtc: '2026-10-17T05:00:00Z', timeZoneId: 'America/Lima' }
export const reports: ReportTypes = {
  overview: { ...base, periodStartUtc: time, periodEndExclusiveUtc: period.endExclusiveUtc, branches: [{ ...overviewMetrics, branchId: workspace.branchId, branchName: workspace.branch.name, isMainHub: false, productsWithAvailableStockCount: n('1') }], consolidatedTotals: { ...overviewMetrics, branchProductAvailabilityCount: n('1') } },
  dashboard: { ...scoped, period, averageTicketAmount: n('1.0001'), metrics: { netSalesAmount: overviewMetrics.netSalesAmount, confirmedSaleCount: overviewMetrics.confirmedSaleCount, voidedSaleCount: n('0'), openCashSessionCount: n('0'), currentInventoryCapitalAmount: n('2.0001'), expiredInventoryCapitalAmount: n('0'), expiringInventoryCapitalAmount: n('0'), expiringLotCount: n('0'), expiredLotCount: n('0'), criticalStockProductCount: n('0') } },
  sales: { ...scoped, ...page, period, dimension: 'branch', sort: 'sales_amount_desc', employeeMembershipId: null, businessProductId: null, categoryId: null, rows: [{ groupId: workspace.branchId, groupName: workspace.branch.name, salesAmount: n('0.0001'), saleCount: n('1'), baseQuantitySold: null }], totals: { groupCount: n('1'), distinctSaleCount: n('1'), salesAmount: n('0.0001') }, amountBasis: 'whole_sale_headers' },
  'stock-risk': { ...scoped, ...page, todayLocal: '2026-10-16', businessProductId: null, criticalOnly: false, rows: [{ branchId: workspace.branchId, branchName: workspace.branch.name, businessProductId: detail.product.id, productName: detail.product.name, isProductActive: true, physicalStockBase: n('2'), sellableStockBase: n('1'), minimumStockBase: null, isCritical: false, thresholdStatus: 'not_configured' }], totals: { branchProductCount: n('1'), criticalStockProductCount: n('0') } },
  expirations: { ...scoped, ...page, todayLocal: '2026-10-16', fromExpirationDate: '2026-10-16', toExpirationDate: '2027-01-01', rows: [{ inventoryLotId: id(30), businessProductId: detail.product.id, productName: detail.product.name, branchId: workspace.branchId, batchNumber: 'BATCH-UNIT', expirationDate: '2026-10-16', quantityAvailableBase: n('1.000000000001'), state: 'expiring' }], totals: { lotCount: n('1'), expiredLotCount: n('0'), expiringLotCount: n('1'), laterLotCount: n('0') } },
  'inventory-capital': { ...scoped, todayLocal: '2026-10-16', branches: [{ branchId: workspace.branchId, totalInventoryCapital: n('0.0001'), expiredInventoryCapital: n('0'), expiringInventoryCapital: n('0.0001') }], totals: { totalInventoryCapital: n('0.0001'), expiredInventoryCapital: n('0'), expiringInventoryCapital: n('0.0001') } },
  'product-rotation': { ...scoped, ...page, period, mode: 'top', rows: [{ businessProductId: detail.product.id, productName: detail.product.name, categoryId: id(8), salesCount: n('1'), baseQuantitySold: n('0.000000000001'), salesAmount: n('0.0001'), currentSellableStockBase: n('2') }], totals: { productCount: n('1'), salesAmount: n('0.0001') } },
  commissions: { ...base, ...page, periodStartUtc: time, periodEndExclusiveUtc: period.endExclusiveUtc, rows: [{ commissionEntryId: id(501), saleId: id(502), saleLineId: id(503), branchId: workspace.branchId, sellerMembershipId: workspace.access.membershipId, sellerName: 'Vendedor unitario', businessProductId: detail.product.id, productName: detail.product.name, confirmedAtUtc: time, originalEarnedAmount: n('0.1234'), compensatedAmount: n('0.1234'), currentNetAmount: n('0'), reversedAtUtc: '2026-11-01T12:00:00Z' }], totals: { earnedEntryCount: n('1'), originalEarnedAmount: n('0.1234'), compensatedAmount: n('0.1234'), currentNetAmount: n('0') } },
}
export function reportingServer() {
  const fake = transferServer(); const parent = fake.fetcher.getMockImplementation()!
  const state = { values: { ...reports }, reject: 0, delay: null as Promise<Response> | null, exports: 0 }
  fake.fetcher.mockImplementation(async (path, init) => {
    const url = new URL(String(path), 'https://unit.test'); const kind = url.pathname.split('/').at(-1) as ReportKind
    if (url.pathname.endsWith('/categories')) return json([{ id: id(8), name: 'Categoría unitaria', isActive: true, createdAt: time }])
    if (url.pathname.endsWith('/products')) return json([detail.product, { ...detail.product, id: id(801), name: 'Producto inactivo unitario', isActive: false }])
    if (url.pathname.endsWith('/employees')) return json([{ membershipId: workspace.access.membershipId, userId: workspace.session.userId, displayName: 'Vendedor unitario', role: 'owner', isActive: true, createdAt: time, branchIds: [workspace.branchId], schedule: [] }])
    if (url.pathname.includes('/reports/')) {
      if (state.reject) return json({ code: 'reports.unit_error' }, state.reject)
      if (state.delay) return state.delay
      if (url.pathname.endsWith('/exports')) { state.exports++; return new Response('%PDF-1.7\nSYNTHETIC-REPORT-UNIT', { headers: { 'Content-Type': 'application/pdf' } }) }
      const value = state.values[kind]
      const result = { ...value, ...('branchId' in value ? { branchId: url.searchParams.get('branchId') } : {}), ...('offset' in value ? { offset: Number(url.searchParams.get('offset') ?? '0'), limit: 20 } : {}) }
      if ('rows' in value) Object.assign(result, { rows: value.rows.slice(Number(url.searchParams.get('offset') ?? '0'), Number(url.searchParams.get('offset') ?? '0') + 20) })
      if ('period' in value) Object.assign(result, { period: { ...value.period, type: url.searchParams.get('period'), ...(url.searchParams.get('period') === 'custom' ? { fromLocalDate: url.searchParams.get('from'), toLocalDate: url.searchParams.get('to') } : {}) } })
      if (kind === 'expirations') Object.assign(result, { fromExpirationDate: url.searchParams.get('from'), toExpirationDate: url.searchParams.get('to') })
      if (kind === 'stock-risk') Object.assign(result, { businessProductId: url.searchParams.get('productId'), criticalOnly: url.searchParams.get('criticalOnly') === 'true' })
      if (kind === 'product-rotation') Object.assign(result, { mode: url.searchParams.get('mode') })
      if (kind === 'sales') Object.assign(result, { dimension: url.searchParams.get('dimension'), sort: url.searchParams.get('sort'), employeeMembershipId: url.searchParams.get('employeeMembershipId'), businessProductId: url.searchParams.get('productId'), categoryId: url.searchParams.get('categoryId'), amountBasis: url.searchParams.has('productId') || url.searchParams.has('categoryId') ? 'matching_sale_lines' : 'whole_sale_headers' })
      return json(result)
    }
    return parent(path, init)
  })
  return { ...fake, reportState: state }
}
