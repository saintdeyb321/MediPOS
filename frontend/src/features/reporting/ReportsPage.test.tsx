import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ReportsPage } from './ReportsPage'
import { reportLabels } from './filters'
import type { ReportKind } from './contracts'
import { reportingServer, mountTransfers, workspace, reports, id, n, json } from './reporting.test.support'

async function choose(kind: ReportKind) { fireEvent.change(screen.getByRole('combobox', { name: 'Reporte' }), { target: { value: kind } }); const apply = screen.getByRole('button', { name: 'Aplicar filtros y consultar' }); await waitFor(() => expect(apply).toBeEnabled()); fireEvent.click(apply); await screen.findByRole('heading', { name: reportLabels[kind] + ' · consulta aplicada' }); await waitFor(() => expect(screen.queryByText('Consultando reporte…')).not.toBeInTheDocument()) }
it('loads overview first, preserves large exact metrics and applies dashboard with only named-period parameters', async () => {
  const fake = reportingServer(); mountTransfers(fake, <ReportsPage />)
  await screen.findByRole('table', { name: 'Resumen por sucursal · filas del servidor' }); expect(screen.getAllByText('S/ 9007199254740993.1234').length).toBeGreaterThan(0)
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/overview?'))).toBe(true)
  await choose('dashboard'); await screen.findByText(/promedio y redondeo son del servidor/)
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/dashboard?branchId=') && String(path).endsWith('&period=day'))).toBe(true)
  expect(screen.getByText('S/ 1.0001')).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it.each(['pharmacist', 'cashier'] as const)('denies %s before fetching report or lookup data', role => {
  const fake = reportingServer(); mountTransfers(fake, <ReportsPage />, '', { ...workspace, access: { ...workspace.access, role } })
  expect(screen.getByText(/requieren Owner/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('displays all report contracts and empty rows separately from failed queries without creating commercial data', async () => {
  const fake = reportingServer(); fake.reportState.values.sales = { ...reports.sales, rows: [] }; mountTransfers(fake, <ReportsPage />)
  await screen.findByRole('table', { name: /Resumen por sucursal/ }); await choose('sales'); await screen.findByText(/No hay filas para los filtros/)
  for (const kind of ['stock-risk', 'expirations', 'inventory-capital', 'product-rotation', 'commissions'] as const) await choose(kind)
  expect(screen.getByText(/regla vigente no recalcula el historial/)).toBeInTheDocument()
  fake.reportState.reject = 503; fireEvent.click(within(screen.getByRole('region', { name: 'Resultado del reporte aplicado' })).getByRole('button', { name: 'Actualizar consulta' })); await screen.findByText('No pudimos consultar el reporte')
  expect(screen.queryByText(/No hay filas para los filtros/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('uses server totals for paging and keeps draft changes separate from the applied results', async () => {
  const fake = reportingServer(); fake.reportState.values.sales = { ...reports.sales, rows: Array.from({ length: 21 }, (_, index) => ({ ...reports.sales.rows[0]!, groupId: id(700 + index), groupName: 'Grupo unitario ' + index })), totals: { ...reports.sales.totals, groupCount: n('21') } }
  mountTransfers(fake, <ReportsPage />); await screen.findByRole('table', { name: /Resumen por sucursal/ }); await choose('sales')
  const next = within(screen.getByRole('region', { name: 'Resultado del reporte aplicado' })).getByRole('button', { name: 'Página siguiente' }); await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next)
  await screen.findByText('Grupo unitario 20'); expect(screen.queryByText('Grupo unitario 0')).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/sales?') && String(path).includes('offset=20&limit=20'))).toBe(true)
  const before = fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/reports/')).length
  fireEvent.change(screen.getByRole('combobox', { name: 'Periodo Lima' }), { target: { value: 'month' } }); expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/reports/')).length).toBe(before)
})
it('validates bounded date ranges before sending, while exposing only actual directory options', async () => {
  const fake = reportingServer(); mountTransfers(fake, <ReportsPage />); await screen.findByRole('table', { name: /Resumen por sucursal/ })
  fireEvent.change(screen.getByLabelText('Desde (Lima)'), { target: { value: '2026-01-01' } }); fireEvent.change(screen.getByLabelText('Hasta (Lima)'), { target: { value: '2026-02-01' } })
  expect(screen.getByRole('button', { name: 'Aplicar filtros y consultar' })).toBeDisabled(); expect(screen.getByRole('combobox', { name: 'Sucursal del reporte' })).toHaveTextContent(workspace.branch.name)
  expect(screen.queryByRole('textbox', { name: /UUID|tenantId|membershipId/i })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('filters historical inactive products, real directory sellers and categories with the backend parameter names', async () => {
  const fake = reportingServer(); mountTransfers(fake, <ReportsPage />); await screen.findByRole('table', { name: /Resumen por sucursal/ }); await choose('sales')
  await screen.findByRole('option', { name: 'Producto inactivo unitario · inactivo' }); await waitFor(() => expect(screen.getByRole('combobox', { name: 'Empleado/vendedor opcional' })).toBeEnabled())
  fireEvent.change(screen.getByRole('combobox', { name: 'Producto opcional del negocio' }), { target: { value: id(801) } }); fireEvent.change(screen.getByRole('combobox', { name: 'Empleado/vendedor opcional' }), { target: { value: workspace.access.membershipId } }); fireEvent.change(screen.getByRole('combobox', { name: 'Categoría opcional' }), { target: { value: id(8) } }); fireEvent.click(screen.getByRole('button', { name: 'Aplicar filtros y consultar' }))
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('productId=' + id(801)) && String(path).includes('employeeMembershipId=' + workspace.access.membershipId) && String(path).includes('categoryId=' + id(8)))).toBe(true))
  await screen.findByText(/Importes de las líneas que coinciden/); expect(fake.writes()).toHaveLength(0)
})
it('cancels old report reads on context change and never displays late old-branch results', async () => {
  const fake = reportingServer(); let finish!: (value: Response) => void; fake.reportState.delay = new Promise<Response>(done => { finish = done }); const view = mountTransfers(fake, <ReportsPage />)
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/overview?'))).toBe(true))
  const signal = fake.fetcher.mock.calls.find(([path]) => String(path).includes('/reports/overview?'))![1]!.signal!; fake.reportState.delay = null
  view.changeContext({ ...workspace, access: { ...workspace.access, membershipId: id(900) }, session: { userId: id(901) } }); expect(signal.aborted).toBe(true)
  await act(async () => finish(json({ ...reports.overview, branches: [{ ...reports.overview.branches[0]!, branchName: 'Late previous private branch' }] })))
  expect(screen.queryByText('Late previous private branch')).not.toBeInTheDocument()
})
it('downloads only after explicit user action, using the applied filters, CSRF and binary transport once', async () => {
  const fake = reportingServer(); mountTransfers(fake, <ReportsPage />); await screen.findByRole('table', { name: /Resumen por sucursal/ }); expect(screen.queryByRole('button', { name: 'Descargar PDF' })).not.toBeInTheDocument()
  await choose('sales'); const area = within(await screen.findByRole('region', { name: 'Exportar reporte' })); expect(area.getByRole('button', { name: 'Descargar Excel' })).toBeEnabled()
  const create = vi.fn(() => 'blob:synthetic-unit'); const revoke = vi.fn(); Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: create }); Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revoke })
  // Undici Response.blob and jsdom Blob have different constructors; align the test environment only.
  const binaryBlob = (await new Response('synthetic').blob()).constructor
  vi.stubGlobal('Blob', binaryBlob); const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
  try {
    expect(fake.reportState.exports).toBe(0); const button = area.getByRole('button', { name: 'Descargar PDF' }); fireEvent.click(button); fireEvent.click(button)
    await screen.findByText(/Archivo generado con todos los filtros/); expect(fake.reportState.exports).toBe(1); expect(create).toHaveBeenCalledTimes(1); expect(click).toHaveBeenCalledTimes(1)
    const request = fake.writes()[0]![1]!; expect(request.body).toContain('"reportType":"sales"'); expect(request.body).toContain('"format":"pdf"'); expect(request.body).not.toContain('offset'); expect(new Headers(request.headers).get('X-CSRF-TOKEN')).toBe('transfers-unit-csrf')
  } finally { click.mockRestore(); vi.unstubAllGlobals() }
})
