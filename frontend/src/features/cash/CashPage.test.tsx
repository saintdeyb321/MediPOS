import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { createPreviewService } from '../../development/fixtures'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { decimal, object } from '../../shared/api/contracts'
import { CashPage } from './CashPage'

// Component/transport doubles only; these do not execute PostgreSQL or a real cash close.
const id = (suffix: string) => `019a0162-0000-7000-8000-${suffix.padStart(12, '0')}`
const tenantId = id('2'); const branchId = id('5'); const userId = id('1'); const membershipId = id('3'); const cashId = id('30')
const n = (value: string) => new JsonNumber(value)
const timestamp = '2026-10-16T12:00:00Z'
const session = { cashSessionId: cashId, tenantId, branchId, membershipId, openingAmount: n('0.0000'), openedAt: timestamp, openedByActorId: userId }
const workspace: Workspace = { tenantId, branchId, tenantName: 'Prueba unitaria', base: `/boticas/${tenantId}/sucursales/${branchId}`,
  session: { userId }, branch: { id: branchId, tenantId, legalEntityId: id('4'), name: 'Sede unitaria', isMainHub: false, createdAt: timestamp },
  access: { userId, tenantId, branchId, membershipId, role: 'owner', membershipIsActive: true, branchAssigned: true, withinSchedule: true,
    licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: timestamp } }
function json(data: unknown, status = 200) { return new Response(stringifyExactJson(data), { status, headers: { 'Content-Type': 'application/json' } }) }
function server() {
  const state = { closed: false, loseClose: false, storeClose: true, denied: false, failReceipt: false, foreignReceipt: false, wrongAckActor: false, statusFailure: 0, counted: n('5.00') }
  const receipt = () => ({ cashSessionId: cashId, tenantId: state.foreignReceipt ? id('99') : tenantId,
    employeeBranch: { branchId, branchName: 'Sede unitaria', membershipId, userId, displayName: 'Empleado unitario' },
    openingAmount: n('0.0000'), paymentTotals: { cash: n('5.0000'), yape: n('7.0000'), plin: n('0'), card: n('0'), transfer: n('0') },
    netSalesAmount: n('12.0000'), expectedCashAmount: n('5.0000'), countedCashAmount: state.counted, cashDifference: n('0.0000'),
    openedAt: timestamp, closedAt: timestamp, closedByActorId: userId, cashTransfers: { incoming: n('0'), outgoing: n('0') } })
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input)
    if (path === '/auth/csrf') return json({ requestToken: 'unit-csrf' })
    if (path.endsWith('/cash-sessions/current')) return json({ session: state.closed ? null : session })
    if (path.includes('/cash-sessions/active?')) return json(state.closed ? [] : [{ cashSessionId: cashId, branchId, branchName: 'Sede unitaria', membershipId, userId,
      displayName: 'Empleado unitario', openingAmount: n('0'), openedAt: timestamp, accumulatedSales: n('12') }])
    if (path.endsWith('/close')) {
      if (state.denied) return json({ code: 'cash_session.forbidden_close' }, 403)
      state.counted = object({ countedCashAmount: decimal })(parseExactJson(String(init?.body))).countedCashAmount
      if (state.storeClose) state.closed = true
      if (state.loseClose) throw new TypeError('Unit-test response loss')
      return json({ ...receipt(), closedByActorId: state.wrongAckActor ? id('98') : userId })
    }
    if (path.endsWith('/reconciliation')) return state.failReceipt ? json({ code: 'server.unavailable' }, 503) : json(receipt())
    if (path.endsWith('/' + cashId)) return state.statusFailure
      ? json({ code: 'test.status_failed' }, state.statusFailure)
      : json({ ...session, status: state.closed ? 'closed' : 'open', closedAt: state.closed ? timestamp : null, expectedCashAmount: state.closed ? n('5') : null })
    throw new Error('Unexpected test route')
  })
  const gets = (suffix: string) => fetcher.mock.calls.filter(([path, init]) => init?.method !== 'POST' && String(path).endsWith(suffix))
  return { state, fetcher, posts: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST'),
    statusGets: () => gets('/' + cashId), receiptGets: () => gets('/reconciliation') }
}
function Location() { return <output data-testid="location">{useLocation().search}</output> }
function mount(fake: ReturnType<typeof server>, search = '', role: 'owner' | 'cashier' = 'owner') {
  return render(<RuntimeProvider runtime={{ service: createPreviewService(role), http: new HttpClient(fake.fetcher), preview: false }}>
    <MemoryRouter initialEntries={['/caja' + search]}><WorkspaceContext.Provider value={{ ...workspace, access: { ...workspace.access, role } }}><CashPage /><Location /></WorkspaceContext.Provider></MemoryRouter>
  </RuntimeProvider>)
}
async function prepare(fake: ReturnType<typeof server>) {
  const view = mount(fake)
  const button = await screen.findByRole('button', { name: 'Preparar cierre de mi caja' })
  await waitFor(() => expect(button).toBeEnabled()); fireEvent.click(button)
  await screen.findByRole('form', { name: 'Cierre de caja' })
  expect(fake.posts()).toHaveLength(0)
  fireEvent.change(screen.getByRole('textbox', { name: 'Efectivo físico contado (S/)' }), { target: { value: '5.00' } })
  fireEvent.submit(screen.getByRole('form', { name: 'Cierre de caja' }))
  expect(fake.posts()).toHaveLength(0)
  return view
}
it('closes only on explicit activation and uses the complete scoped reconciliation without a second read', async () => {
  const fake = server(); await prepare(fake)
  const button = screen.getByRole('button', { name: 'Confirmar cierre de caja' })
  fireEvent.click(button); fireEvent.click(button)
  await screen.findByRole('region', { name: 'Arqueo registrado' })
  expect(fake.posts()).toHaveLength(1)
  expect(fake.posts()[0]?.[1]?.body).toBe('{"countedCashAmount":5.00}')
  expect(new Headers(fake.posts()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-csrf')
  expect(screen.getByText('Cuadre exacto')).toBeInTheDocument()
  expect(fake.statusGets()).toHaveLength(1)
  expect(fake.receiptGets()).toHaveLength(0)
  expect(screen.queryByRole('button', { name: 'Confirmar cierre de caja' })).not.toBeInTheDocument()
})
it('reconciles a lost close response without repeating the POST', async () => {
  const fake = server(); fake.state.loseClose = true; await prepare(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByRole('region', { name: 'Arqueo registrado' })
  expect(fake.posts()).toHaveLength(1)
})
it('keeps uncertain closure blocked across reload while still open, then reads a late close', async () => {
  const fake = server(); fake.state.loseClose = true; fake.state.storeClose = false; const view = await prepare(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText('Revisa el estado de la caja')
  expect(screen.getAllByText(/No pudimos confirmar el resultado/).length).toBeGreaterThan(0)
  await waitFor(() => expect(fake.statusGets()).toHaveLength(2))
  const search = screen.getByTestId('location').textContent!
  expect(search).toContain('closingCash')
  expect(fake.posts()).toHaveLength(1)
  expect(fake.statusGets()).toHaveLength(2)
  expect(fake.receiptGets()).toHaveLength(0)
  view.unmount(); mount(fake, search)
  expect(screen.queryByRole('button', { name: 'Abrir mi caja' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Consultar cierre guardado' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar cierre guardado' })).toBeEnabled())
  expect(screen.getByTestId('location')).toHaveTextContent('closingCash')
  expect(screen.queryByRole('button', { name: 'Confirmar cierre de caja' })).not.toBeInTheDocument()
  expect(fake.posts()).toHaveLength(1)
  expect(fake.statusGets()).toHaveLength(3)
  expect(fake.receiptGets()).toHaveLength(0)
  fake.state.closed = true
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Consultar cierre guardado' })) })
  await screen.findByRole('region', { name: 'Arqueo registrado' })
  expect(fake.posts()).toHaveLength(1)
  expect(fake.statusGets()).toHaveLength(4)
  expect(fake.receiptGets()).toHaveLength(1)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('closingCash'))
})
it('does not expose Owner active-cash queries to a cashier', async () => {
  const fake = server(); mount(fake, '', 'cashier')
  await screen.findByText('Caja abierta')
  expect(screen.queryByRole('heading', { name: 'Cajas activas' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/active?'))).toBe(false)
  expect(fake.posts()).toHaveLength(0)
})
it.each([401, 403, 404, 429, 503])('keeps the same closure pending when status GET returns %i after an unverifiable POST response', async status => {
  const fake = server(); await prepare(fake); fake.state.wrongAckActor = true; fake.state.statusFailure = status
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText('Revisa el estado de la caja')
  expect(screen.getByTestId('location')).toHaveTextContent('cash=' + cashId + '&closingCash=1')
  expect(screen.queryByRole('form', { name: 'Cierre de caja' })).not.toBeInTheDocument()
  expect(screen.queryByText('Caja cerrada en el servidor.', { exact: false })).not.toBeInTheDocument()
  expect(fake.statusGets()).toHaveLength(2)
  expect(fake.receiptGets()).toHaveLength(0)
  const retry = screen.getByRole('button', { name: 'Consultar esta misma caja' })
  await waitFor(() => expect(retry).toBeEnabled())
  fireEvent.click(retry)
  await waitFor(() => expect(fake.statusGets()).toHaveLength(3))
  await screen.findByText('Revisa el estado de la caja')
  expect(screen.getByTestId('location')).toHaveTextContent('cash=' + cashId + '&closingCash=1')
  expect(screen.queryByRole('form', { name: 'Cierre de caja' })).not.toBeInTheDocument()
  expect(fake.receiptGets()).toHaveLength(0)
  expect(fake.posts()).toHaveLength(1)
})
it('keeps the verified closed status and retries only the failed reconciliation GET', async () => {
  const fake = server(); fake.state.failReceipt = true; fake.state.loseClose = true; await prepare(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText('Revisa el estado de la caja')
  expect(screen.getByText(/Caja cerrada en el servidor/)).toBeInTheDocument()
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('closingCash'))
  expect(screen.queryByRole('form', { name: 'Cierre de caja' })).not.toBeInTheDocument()
  expect(fake.statusGets()).toHaveLength(2)
  expect(fake.receiptGets()).toHaveLength(1)
  fake.state.failReceipt = false
  const consult = screen.getByRole('button', { name: 'Consultar cierre guardado' })
  await waitFor(() => expect(consult).toBeEnabled())
  fireEvent.click(consult)
  await screen.findByRole('region', { name: 'Arqueo registrado' })
  expect(fake.statusGets()).toHaveLength(2)
  expect(fake.receiptGets()).toHaveLength(2)
  expect(fake.posts()).toHaveLength(1)
})
it('clears the marker for an actual POST denial and keeps closing disabled', async () => {
  const fake = server(); fake.state.denied = true; await prepare(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText('Revisa el estado de la caja')
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('closingCash'))
  expect(screen.queryByRole('form', { name: 'Cierre de caja' })).not.toBeInTheDocument()
  expect(fake.state.closed).toBe(false)
  expect(fake.statusGets()).toHaveLength(2)
  expect(fake.receiptGets()).toHaveLength(0)
  expect(fake.posts()).toHaveLength(1)
})
it('rejects reconciliation belonging to another tenant without displaying its data', async () => {
  const fake = server(); fake.state.foreignReceipt = true; await prepare(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar cierre de caja' }))
  await screen.findByText('Revisa el estado de la caja')
  expect(screen.queryByRole('region', { name: 'Arqueo registrado' })).not.toBeInTheDocument()
  expect(fake.posts()).toHaveLength(1)
})
