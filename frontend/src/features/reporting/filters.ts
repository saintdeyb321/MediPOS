import { dateOnly, uuid } from '../../shared/api/contracts'
import { JsonNumber } from '../../shared/api/exactJson'
import { compare } from '../../shared/api/decimal'
import type { Dimension, PeriodKind, ReportKind, SalesSort } from './contracts'

export const reportLabels: Record<ReportKind, string> = { overview: 'Resumen por sucursal', dashboard: 'Dashboard operacional', sales: 'Ventas', 'stock-risk': 'Stock crítico', expirations: 'Vencimientos', 'inventory-capital': 'Capital en inventario', 'product-rotation': 'Rotación de productos', commissions: 'Comisiones devengadas' }
export interface ReportFilters { kind: ReportKind; branchId: string; from: string; to: string; period: PeriodKind; dimension: Dimension; sort: SalesSort; productId: string; employeeId: string; categoryId: string; criticalOnly: boolean; rotation: 'top' | 'low_activity'; offset: number }
export function todayLima(now = new Date()) { const parts = new Intl.DateTimeFormat('en', { timeZone: 'America/Lima', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now); const get = (key: string) => parts.find(part => part.type === key)!.value; return `${get('year')}-${get('month')}-${get('day')}` }
export function initialFilters(branchId: string): ReportFilters { const today = todayLima(); return { kind: 'overview', branchId, from: today, to: today, period: 'day', dimension: 'branch', sort: 'sales_amount_desc', productId: '', employeeId: '', categoryId: '', criticalOnly: false, rotation: 'top', offset: 0 } }
export const usesDates = (f: ReportFilters) => ['overview', 'commissions', 'expirations'].includes(f.kind) || ['dashboard', 'sales', 'product-rotation'].includes(f.kind) && f.period === 'custom'
export function validDates(from: string, to: string, limit: number) { try { dateOnly(from); dateOnly(to); const days = (Date.parse(to + 'T00:00:00Z') - Date.parse(from + 'T00:00:00Z')) / 86400000; return days >= 0 && days < limit && to < '9999-12-31' } catch { return false } }
export function allowedSort(dimension: Dimension, sort: SalesSort) { return ['sales_amount_desc', 'sales_amount_asc', 'sale_count_desc', 'sale_count_asc'].includes(sort) || sort === `${dimension}_name_asc` && dimension !== 'category' }
export function validFilters(f: ReportFilters) { try { for (const id of [f.branchId, f.productId, f.employeeId, f.categoryId]) if (id) uuid(id); return (!usesDates(f) || validDates(f.from, f.to, f.kind === 'overview' || f.kind === 'commissions' ? 31 : 366)) && (f.kind !== 'sales' || allowedSort(f.dimension, f.sort)) && Number.isInteger(f.offset) && f.offset >= 0 && f.offset <= 10000 } catch { return false } }
export function reportQuery(f: ReportFilters) {
  if (!validFilters(f)) throw new RangeError('Invalid report filters')
  const query = new URLSearchParams(); if (f.branchId) query.set('branchId', f.branchId)
  if (usesDates(f)) { query.set('from', f.from); query.set('to', f.to) }
  if (['dashboard', 'sales', 'product-rotation'].includes(f.kind)) query.set('period', f.period)
  if (['sales', 'stock-risk', 'commissions'].includes(f.kind) && f.productId) query.set('productId', f.productId)
  if (f.kind === 'sales') { query.set('dimension', f.dimension); query.set('sort', f.sort); if (f.categoryId) query.set('categoryId', f.categoryId) }
  if (['sales', 'commissions'].includes(f.kind) && f.employeeId) query.set(f.kind === 'sales' ? 'employeeMembershipId' : 'sellerMembershipId', f.employeeId)
  if (f.kind === 'stock-risk') query.set('criticalOnly', String(f.criticalOnly))
  if (f.kind === 'product-rotation') query.set('mode', f.rotation)
  if (!['overview', 'dashboard', 'inventory-capital'].includes(f.kind)) { query.set('offset', String(f.offset)); query.set('limit', '20') }
  return query
}
export const moreRows = (count: JsonNumber, offset: number, shown: number) => compare(count, new JsonNumber(String(offset + shown))) > 0
export function exportBody(f: ReportFilters, format: 'pdf' | 'xlsx') {
  if (!validFilters(f) || f.kind === 'overview' || f.kind === 'dashboard') throw new RangeError('Report has no export contract')
  return { reportType: f.kind.replaceAll('-', '_'), format, branchId: f.branchId || null,
    ...(['sales', 'commissions', 'product-rotation'].includes(f.kind) ? { period: { type: f.kind === 'commissions' ? 'custom' : f.period, ...(f.kind === 'commissions' || f.period === 'custom' ? { fromLocalDate: f.from, toLocalDate: f.to } : {}) } } : {}),
    ...(f.kind === 'sales' ? { dimension: f.dimension, sort: f.sort, ...(f.categoryId ? { categoryId: f.categoryId } : {}) } : {}),
    ...(['sales', 'commissions'].includes(f.kind) && f.employeeId ? { employeeMembershipId: f.employeeId } : {}),
    ...(['sales', 'commissions', 'stock-risk'].includes(f.kind) && f.productId ? { businessProductId: f.productId } : {}),
    ...(f.kind === 'stock-risk' ? { criticalOnly: f.criticalOnly } : {}), ...(f.kind === 'expirations' ? { expirationRange: { from: f.from, to: f.to } } : {}), ...(f.kind === 'product-rotation' ? { rotationMode: f.rotation } : {}) }
}
