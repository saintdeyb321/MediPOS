import { describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { focusManager } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { AppRoutes } from '../../app/router/routes'
import { WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { createPreviewService } from '../../development/fixtures'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { array, decimal, object, oneOf, uint, uuid } from '../../shared/api/contracts'
import { multiply, roundEven4, add, decimalLabel, zero } from '../../shared/api/decimal'
import type { ProductMatch } from '../catalog'
import type { OpenCashSession } from '../cash'
import type { SaleLineInput, SaleState, SalePaymentInput } from './sales.api'
import type { InternalTicket } from './tickets.api'
import { PosPage } from './PosPage'

// Synthetic component/transport tests only; real PostgreSQL/Google acceptance is separate.
const id = (last: string) => `019a0162-0000-7000-8000-${last.padStart(12, '0')}`
const tenantId = id('2'); const branchId = id('5'); const userId = id('1'); const membershipId = id('3'); const saleId = id('40')
const n = (text: string) => new JsonNumber(text)
const product: ProductMatch = { businessProductId: id('11'), name: 'Producto de prueba unitaria', productType: 'retail', brandOrLaboratory: 'Marca', internalCode: 'TEST', barcode: null,
  retailPrice: n('0.1'), wholesalePrice: null, quantityAvailableBase: n('20'), units: [
    { id: id('12'), name: 'Tableta', conversionToBase: n('1'), isBaseUnit: true, isActive: true },
    { id: id('13'), name: 'Caja de 10', conversionToBase: n('10'), isBaseUnit: false, isActive: true },
  ] }
const cash: OpenCashSession = { cashSessionId: id('30'), tenantId, branchId, membershipId, openingAmount: n('0.10'), openedAt: '2026-10-15T00:00:00Z', openedByActorId: userId }
const emptySale = (): SaleState => ({ saleId, tenantId, branchId, cashSessionId: cash.cashSessionId, sellerMembershipId: membershipId,
  status: 'draft', totalAmount: n('0'), version: 1, createdAt: '2026-10-15T00:00:00Z', updatedAt: '2026-10-15T00:00:00Z', lines: [] })
function withLines(quantity = '2', version = 2): SaleState {
  return { ...emptySale(), version, totalAmount: roundEven4(multiply(n(quantity), product.retailPrice)), lines: [{ saleLineId: id('41'),
    businessProductId: product.businessProductId, productUnitIdSnapshot: product.units[0]!.id, productNameSnapshot: product.name,
    quantity: n(quantity), baseQuantity: n(quantity), unitNameSnapshot: 'Tableta', conversionToBaseSnapshot: n('1'), priceKind: 'retail',
    unitPriceSnapshot: product.retailPrice, lineTotal: roundEven4(multiply(n(quantity), product.retailPrice)) }] }
}
function context(): Workspace {
  return { tenantId, branchId, tenantName: 'Unit test', base: `/boticas/${tenantId}/sucursales/${branchId}`,
    session: { userId }, branch: { id: branchId, tenantId, legalEntityId: id('4'), name: 'Sede unitaria', isMainHub: false, createdAt: '2026-10-15T00:00:00Z' },
    access: { userId, tenantId, branchId, membershipId, role: 'owner', membershipIsActive: true, branchAssigned: true, withinSchedule: true,
      licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: '2026-10-15T00:00:00Z' } }
}
function json(data: unknown, status = 200) { return new Response(stringifyExactJson(data), { status, headers: { 'Content-Type': 'application/json' } }) }
const lineInput = object<SaleLineInput>({ businessProductId: uuid, productUnitId: uuid, quantity: decimal, priceKind: oneOf('retail', 'wholesale') })
function server() {
  const state = { cash: cash as OpenCashSession | null, sale: null as SaleState | null, loseOpen: false, storeOpen: true, loseCreate: false,
    loseSave: false, conflict: false, denySave: false, failReads: false,
    loseConfirm: false, storeConfirm: true, confirmFailure: 0, confirmConflict: false, failTicket: false, malformedConfirm: false,
    loseVoid: false, storeVoid: true, voidFailure: 0, payments: [] as SalePaymentInput[] }
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'
    if (path === '/auth/csrf') return json({ requestToken: 'unit-test-csrf' })
    if (path.includes('/products/search?')) return json({ directMatches: [product], equivalentInBranch: [], otherBranchAvailability: [] })
    if (path.endsWith('/cash-sessions/current')) return json({ session: state.cash })
    if (method === 'POST' && path.endsWith('/cash-sessions')) {
      if (state.storeOpen) state.cash = cash
      if (state.loseOpen) throw new TypeError('unit-test lost response')
      return json(cash, 201)
    }
    if (method === 'POST' && path.endsWith('/sales')) { state.sale = emptySale(); if (state.loseCreate) throw new TypeError('unit-test lost response'); return json(state.sale, 201) }
    if (method === 'POST' && path.endsWith('/confirm')) {
      if (state.confirmConflict) { state.sale = withLines('3', 3); return json({ code: 'sale.concurrent_edit' }, 409) }
      if (state.confirmFailure) return json({ code: state.confirmFailure === 403 ? 'access.license_denied' : state.confirmFailure === 409 ? 'inventory.insufficient_stock' : 'api.rate_limited' }, state.confirmFailure)
      const payload = object<{ expectedVersion: number; payments: SalePaymentInput[] }>({ expectedVersion: uint,
        payments: array(object({ method: oneOf('cash', 'yape', 'plin', 'card', 'transfer'), amount: decimal })) })(parseExactJson(String(init?.body)))
      if (state.storeConfirm) { state.sale = { ...state.sale!, status: 'confirmed', version: payload.expectedVersion + 1 }; state.payments = payload.payments }
      if (state.loseConfirm) throw new TypeError('unit-test lost confirmation response')
      if (state.malformedConfirm) return json({ saleId })
      return json({ saleId, totalAmount: state.sale!.totalAmount, confirmedAt: '2026-10-16T12:00:00Z', version: state.sale!.version,
        payments: state.payments.map((payment, index) => ({ ...payment, salePaymentId: id(String(70 + index)) })) })
    }
    if (method === 'POST' && path.endsWith('/void')) {
      if (state.voidFailure) return json({ code: state.voidFailure === 403 ? 'sale.forbidden_void' : 'sale.insufficient_cash' }, state.voidFailure)
      if (state.storeVoid) state.sale = { ...state.sale!, status: 'voided', version: state.sale!.version + 1 }
      if (state.loseVoid) throw new TypeError('Unit-test lost void response')
      return json({ saleId, voidedAt: '2026-10-16T12:01:00Z', version: state.sale!.version, paymentReversalCount: 2, stockReversalCount: 1 })
    }
    if (path.endsWith('/ticket')) {
      if (state.failTicket) return json({ code: 'server.unavailable' }, 503)
      const sale = state.sale!
      const ticket: InternalTicket = { saleId, cashSessionId: cash.cashSessionId, ticketNumber: 'MP-UNIT-TEST',
        currentDisplayData: { tenantId, branchId, businessName: 'Negocio unitario', legalEntityId: id('4'), legalName: 'Prueba', ruc: 'NO-FISCAL',
          branchName: 'Sede unitaria', sellerMembershipId: membershipId, sellerUserId: userId, sellerDisplayName: 'Vendedor de prueba' },
        saleDateTime: '2026-10-16T12:00:00Z', status: sale.status === 'voided' ? 'voided' : 'confirmed', statusLabel: sale.status === 'voided' ? 'ANULADA' : 'CONFIRMADA', isVoided: sale.status === 'voided', voidedAt: sale.status === 'voided' ? '2026-10-16T12:01:00Z' : null, voidReason: sale.status === 'voided' ? 'Motivo unitario' : null,
        lines: sale.lines.map(line => ({ productName: line.productNameSnapshot, unitName: line.unitNameSnapshot, quantity: line.quantity,
          baseQuantity: line.baseQuantity, conversionToBase: line.conversionToBaseSnapshot, unitPrice: line.unitPriceSnapshot, priceKind: line.priceKind, lineTotal: line.lineTotal, unitPriceBasis: 'BASE_UNIT' })),
        payments: state.payments, totalAmount: sale.totalAmount, documentType: 'INTERNAL_TICKET', footer: 'Documento interno de prueba', supportedPrintWidthsMm: [58, 80] }
      return json(ticket)
    }
    if (method === 'PUT' && path.endsWith('/lines')) {
      if (state.denySave) return json({ code: 'access.license_denied' }, 403)
      if (state.conflict) { state.sale = withLines('3', 2); state.conflict = false; return json({ code: 'sale.concurrent_edit' }, 409) }
      const payload = object<{ expectedVersion: number; lines: SaleLineInput[] }>({ expectedVersion: uint, lines: array(lineInput) })(parseExactJson(String(init?.body)))
      const previous = state.sale!
      state.sale = { ...previous, version: previous.version + 1, lines: payload.lines.map((line, index) => {
        const unit = product.units.find(item => item.id === line.productUnitId)!
        const base = multiply(line.quantity, unit.conversionToBase)
        return { ...withLines().lines[0]!, saleLineId: id(String(50 + index)), productUnitIdSnapshot: unit.id, unitNameSnapshot: unit.name,
          quantity: line.quantity, conversionToBaseSnapshot: unit.conversionToBase, baseQuantity: base, lineTotal: roundEven4(multiply(base, product.retailPrice)) }
      }) }
      state.sale.totalAmount = state.sale.lines.reduce((total, line) => add(total, line.lineTotal), zero)
      if (state.loseSave) throw new TypeError('unit-test lost response')
      return json(state.sale)
    }
    if (method === 'GET' && (path.toLowerCase().endsWith(`/sales/${saleId}`) || path.toLowerCase().endsWith(`/sales/${saleId}/draft`))) {
      if (state.failReads) throw new TypeError('unit-test unavailable read')
      return state.sale ? json(state.sale) : json({ code: 'sale.not_found' }, 404)
    }
    throw new Error('Unexpected unit-test route')
  })
  return { state, fetcher, writes: () => fetcher.mock.calls.filter(([, init]) => init?.method === 'POST' || init?.method === 'PUT') }
}
function Location() { const location = useLocation(); return <output data-testid="location">{location.search}</output> }
function mount(fake: ReturnType<typeof server>, search = '') {
  const runtime = { service: createPreviewService('owner'), http: new HttpClient(fake.fetcher), preview: false }
  const tree = (workspace = context()) => <RuntimeProvider runtime={runtime}><MemoryRouter initialEntries={['/ventas' + search]}><WorkspaceContext.Provider value={workspace}><PosPage /><Location /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>
  const view = render(tree())
  return { ...view, changeContext: (workspace: Workspace) => view.rerender(tree(workspace)) }
}
async function select(quantity = '2') {
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'producto' } })
  const button = await screen.findByRole('button', { name: `Seleccionar ${product.name}` })
  fireEvent.change(screen.getByRole('textbox', { name: /Cantidad a agregar/ }), { target: { value: quantity } })
  fireEvent.click(button)
}
async function createDraft() {
  const button = screen.getByRole('button', { name: 'Cobrar' })
  await waitFor(() => expect(button).toBeEnabled())
  fireEvent.click(button)
  fireEvent.click(button)
  await screen.findByRole('form', { name: 'Pagos de la venta' })
}
async function preparePayment(fake: ReturnType<typeof server>) {
  fake.state.sale = withLines()
  const view = mount(fake, '?sale=' + saleId)
  await waitFor(() => expect(screen.getByRole('button', { name: 'Cobrar' })).toBeEnabled())
  fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
  await screen.findByRole('form', { name: 'Pagos de la venta' })
  fireEvent.change(screen.getByRole('textbox', { name: 'Efectivo (S/)' }), { target: { value: '0.10' } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Yape (S/)' }), { target: { value: '0.10' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé los importes/ }))
  return view
}
async function prepareVoid(fake: ReturnType<typeof server>) {
  const view = await preparePayment(fake)
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
  await screen.findByRole('article', { name: 'Ticket interno' })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Seguir vendiendo sin imprimir' })).toBeEnabled())
  fireEvent.click(screen.getByText('Anular esta venta', { selector: 'summary' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Motivo de anulación' }), { target: { value: ' Motivo unitario ' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé la devolución/ }))
  return view
}

describe('POS local cart, cash and persisted draft', () => {
  it('voids a confirmed sale once with reason/version, reads the voided ticket and leaves its history visible', async () => {
    const fake = server(); await prepareVoid(fake)
    const button = screen.getByRole('button', { name: 'Confirmar anulación' })
    fireEvent.click(button); fireEvent.click(button)
    await screen.findByText('Ticket interno · ANULADA')
    expect(fake.writes()).toHaveLength(2)
    expect(fake.writes()[1]?.[1]?.body).toBe('{"expectedVersion":3,"reason":"Motivo unitario"}')
    expect(fake.state.sale?.status).toBe('voided')
    expect(screen.queryByRole('button', { name: 'Confirmar venta y registrar pagos' })).not.toBeInTheDocument()
  })
  it('does not interpret an early confirmed state as failure of an uncertain void', async () => {
    const fake = server(); fake.state.loseVoid = true; fake.state.storeVoid = false; const view = await prepareVoid(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar anulación' }))
    await screen.findByText(/Que aún aparezca confirmada no demuestra/)
    expect(screen.queryByRole('button', { name: 'Seguir vendiendo sin imprimir' })).not.toBeInTheDocument()
    const search = screen.getByTestId('location').textContent!
    expect(search).toContain('voidPending')
    view.unmount(); mount(fake, search)
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(screen.getByRole('button', { name: 'Confirmar anulación' })).toBeDisabled()
    fake.state.sale = { ...fake.state.sale!, status: 'voided', version: 4 }
    fireEvent.click(screen.getByRole('button', { name: 'Consultar resultado de venta' }))
    await screen.findByText('Ticket interno · ANULADA')
    expect(fake.writes()).toHaveLength(2)
  })
  it.each([403, 409])('does not retry a rejected void %s or claim a reversal', async status => {
    const fake = server(); fake.state.voidFailure = status; await prepareVoid(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar anulación' }))
    await screen.findByText('Revisa el resultado antes de cobrar de nuevo')
    await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('voidPending'))
    expect(fake.writes()).toHaveLength(2)
    expect(fake.state.sale?.status).toBe('confirmed')
    expect(screen.queryByText('Ticket interno · ANULADA')).not.toBeInTheDocument()
  })
  it('confirms mixed exact payments once, reads the ticket, prints only on demand and recovers without another POST', async () => {
    const fake = server(); const view = await preparePayment(fake)
    const print = vi.spyOn(window, 'print').mockImplementation(() => {})
    expect(fake.writes()).toHaveLength(0)
    const confirm = screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })
    fireEvent.click(confirm); fireEvent.click(confirm)
    const ticket = await screen.findByRole('article', { name: 'Ticket interno' })
    expect(within(ticket).getByText('Total registrado: S/ 0.20')).toBeInTheDocument()
    expect(fake.writes()).toHaveLength(1)
    expect(fake.writes()[0]?.[1]?.body).toBe('{"expectedVersion":2,"payments":[{"method":"cash","amount":0.10},{"method":"yape","amount":0.10}]}')
    expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-test-csrf')
    expect(print).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Imprimir ticket interno (opcional)' }))
    expect(print).toHaveBeenCalledTimes(1); print.mockRestore()
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toBeDisabled()
    await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('confirmPending'))
    const search = screen.getByTestId('location').textContent!
    expect(search).not.toContain('confirmPending')
    view.unmount(); mount(fake, search)
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(screen.queryByRole('button', { name: 'Confirmar venta y registrar pagos' })).not.toBeInTheDocument()
    expect(fake.writes()).toHaveLength(1)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Seguir vendiendo sin imprimir' })).toBeEnabled())
    fireEvent.click(screen.getByRole('button', { name: 'Seguir vendiendo sin imprimir' }))
    expect(screen.getByText('Tu carrito está vacío')).toBeInTheDocument()
    expect(screen.getByText('Caja abierta')).toBeInTheDocument()
    expect(screen.getByTestId('location').textContent).toBe('')
    expect(fake.writes()).toHaveLength(1)
  })
  it('requires a saved cart, exact payment sum and explicit review before confirmation', async () => {
    const fake = server(); await preparePayment(fake)
    const confirm = screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })
    fireEvent.change(screen.getByRole('textbox', { name: 'Yape (S/)' }), { target: { value: '0.11' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /Revisé los importes/ }))
    expect(confirm).toBeDisabled()
    fireEvent.change(screen.getByRole('textbox', { name: 'Yape (S/)' }), { target: { value: '0.10' } })
    expect(confirm).toBeDisabled()
    fireEvent.click(screen.getByRole('checkbox', { name: /Revisé los importes/ }))
    expect(confirm).toBeEnabled()
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad en carrito/ }), { target: { value: '3' } })
    expect(confirm).toBeDisabled()
    expect(fake.writes()).toHaveLength(0)
  })
  it.each(['lost', 'malformed'] as const)('reconciles a %s confirmation response by the same sale without resending', async outcome => {
    const fake = server(); fake.state.loseConfirm = outcome === 'lost'; fake.state.malformedConfirm = outcome === 'malformed'
    await preparePayment(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(fake.writes()).toHaveLength(1)
    await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('confirmPending'))
  })
  it('keeps unknown confirmation locked across reload even while reads still show draft, then reconciles a late commit', async () => {
    const fake = server(); fake.state.loseConfirm = true; fake.state.storeConfirm = false
    const view = await preparePayment(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByText(/Incluso si una lectura aún muestra borrador/)
    expect(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })).toBeDisabled()
    const search = screen.getByTestId('location').textContent!
    expect(search).toContain('confirmPending')
    view.unmount(); mount(fake, search)
    await screen.findByText(/Venta recuperada del servidor/)
    expect(screen.queryByRole('button', { name: 'Crear borrador' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar líneas' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    expect(fake.writes()).toHaveLength(1)
    fake.state.sale = { ...fake.state.sale!, status: 'confirmed', version: 3 }
    fireEvent.click(screen.getByRole('button', { name: 'Consultar resultado de venta' }))
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(fake.writes()).toHaveLength(1)
  })
  it('preserves uncertainty when confirmation-state reads fail and recovers with GET only', async () => {
    const fake = server(); await preparePayment(fake); fake.state.loseConfirm = true; fake.state.failReads = true
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByText(/Incluso si una lectura aún muestra borrador/)
    expect(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })).toBeDisabled()
    fake.state.failReads = false
    fireEvent.click(screen.getByRole('button', { name: 'Consultar resultado de venta' }))
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(fake.writes()).toHaveLength(1)
  })
  it.each([403, 409, 429])('does not retry a definitive confirmation rejection %s or claim success', async status => {
    const fake = server(); fake.state.confirmFailure = status; await preparePayment(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByText('Revisa el resultado antes de cobrar de nuevo')
    await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('confirmPending'))
    expect(fake.state.sale?.status).toBe('draft')
    expect(fake.writes()).toHaveLength(1)
    expect(screen.queryByRole('article', { name: 'Ticket interno' })).not.toBeInTheDocument()
    if (status === 403) expect(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })).toBeDisabled()
  })
  it('keeps a confirmed sale final when ticket retrieval fails, and retries the ticket only', async () => {
    const fake = server(); fake.state.failTicket = true; await preparePayment(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByText(/El ticket no pudo recuperarse/)
    expect(screen.queryByRole('button', { name: 'Confirmar venta y registrar pagos' })).not.toBeInTheDocument()
    fake.state.failTicket = false
    fireEvent.click(screen.getByRole('button', { name: 'Consultar ticket' }))
    await screen.findByRole('article', { name: 'Ticket interno' })
    expect(fake.writes()).toHaveLength(1)
  })
  it('reconciles a version conflict and requires reviewing new amounts without resending confirmation', async () => {
    const fake = server(); fake.state.confirmConflict = true; await preparePayment(fake)
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await screen.findByText('Total consultado en el servidor: S/ 0.30')
    expect(screen.getByRole('textbox', { name: 'Efectivo (S/)' })).toHaveValue('')
    expect(screen.getByRole('checkbox', { name: /Revisé los importes/ })).not.toBeChecked()
    expect(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(fake.writes()).toHaveLength(1)
  })
  it('aborts an in-flight confirmation on leaving context and ignores its late response', async () => {
    const fake = server(); const view = await preparePayment(fake)
    const original = fake.fetcher.getMockImplementation()!
    let release!: () => void
    let sentSignal: AbortSignal | null | undefined
    fake.fetcher.mockImplementation(async (input, init) => {
      if (String(input).endsWith('/confirm')) {
        sentSignal = init?.signal
        await new Promise<void>(resolve => { release = resolve })
      }
      return original(input, init)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' }))
    await waitFor(() => expect(sentSignal).toBeDefined())
    view.unmount()
    expect(sentSignal?.aborted).toBe(true)
    const fresh = server(); mount(fresh)
    await act(async () => { release() })
    expect(screen.queryByRole('article', { name: 'Ticket interno' })).not.toBeInTheDocument()
    expect(screen.getByTestId('location').textContent).toBe('')
    expect(fake.writes()).toHaveLength(1)
    expect(fresh.writes()).toHaveLength(0)
  })
  it('retains multiple presentations across searches, edits/removes quantities, and never writes on selection', async () => {
    const fake = server(); mount(fake); await select()
    fireEvent.change(screen.getByRole('combobox', { name: /Presentación de/ }), { target: { value: product.units[1]!.id } })
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad a agregar/ }), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: `Seleccionar ${product.name}` }))
    const cart = within(screen.getByRole('region', { name: 'Carrito local' }))
    expect(cart.getByText('2 líneas')).toBeInTheDocument()
    expect(cart.getByText('S/ 1.20')).toBeInTheDocument()
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: '' } })
    expect(cart.getByText('2 líneas')).toBeInTheDocument()
    fireEvent.change(cart.getByRole('textbox', { name: /Cantidad en carrito.*Tableta/ }), { target: { value: '0.3' } })
    expect(cart.getByText('S/ 1.03')).toBeInTheDocument()
    fireEvent.click(cart.getByRole('button', { name: /Quitar.*Caja de 10/ }))
    expect(cart.getByText('1 líneas')).toBeInTheDocument()
    expect(fake.writes()).toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeEnabled()
    expect(screen.queryByRole('heading', { name: 'Borrador en el servidor' })).not.toBeInTheDocument()
  })
  it('blocks evident aggregate shortage and invalid quantities before any write', async () => {
    const fake = server(); mount(fake); await select('1')
    fireEvent.change(screen.getByRole('combobox', { name: /Presentación de/ }), { target: { value: product.units[1]!.id } })
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad a agregar/ }), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: `Seleccionar ${product.name}` }))
    expect(within(screen.getByRole('region', { name: 'Carrito local' })).getByRole('status')).toHaveTextContent(/supera el stock/)
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad en carrito/ }), { target: { value: '21' } })
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    expect(fake.writes()).toHaveLength(0)
  })
  it('opens cash only on explicit submission, exactly serializes the amount, prevents double click and rereads current cash', async () => {
    const fake = server(); fake.state.cash = null; mount(fake); await select()
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    const button = await screen.findByRole('button', { name: 'Abrir mi caja' })
    fireEvent.change(screen.getByRole('textbox', { name: /Monto inicial/ }), { target: { value: '0.10' } })
    expect(fake.writes()).toHaveLength(0)
    fireEvent.click(button); fireEvent.click(button)
    await screen.findByText('Caja abierta')
    expect(fake.writes()).toHaveLength(1)
    expect(fake.writes()[0]?.[1]?.body).toBe('{"openingAmount":0.10}')
    expect(new Headers(fake.writes()[0]?.[1]?.headers).get('X-CSRF-TOKEN')).toBe('unit-test-csrf')
    expect(fake.fetcher.mock.calls.filter(([path]) => String(path).endsWith('/cash-sessions/current')).length).toBeGreaterThan(1)
  })
  it('reconciles a lost cash-opening response by reads, without a second opening', async () => {
    const fake = server(); fake.state.cash = null; fake.state.loseOpen = true; mount(fake)
    const button = await screen.findByRole('button', { name: 'Abrir mi caja' })
    fireEvent.change(screen.getByRole('textbox', { name: /Monto inicial/ }), { target: { value: '0' } }); fireEvent.click(button)
    await screen.findByText('Caja abierta')
    expect(fake.writes()).toHaveLength(1)
    expect(screen.queryByRole('button', { name: 'Abrir mi caja' })).not.toBeInTheDocument()
  })
  it('keeps an unresolved cash opening blocked across reload when current is still null', async () => {
    const fake = server(); fake.state.cash = null; fake.state.loseOpen = true; fake.state.storeOpen = false
    const view = mount(fake); const button = await screen.findByRole('button', { name: 'Abrir mi caja' })
    fireEvent.change(screen.getByRole('textbox', { name: /Monto inicial/ }), { target: { value: '0' } }); fireEvent.click(button)
    await screen.findByText(/La apertura tiene un resultado pendiente/)
    const search = screen.getByTestId('location').textContent!; view.unmount(); mount(fake, search)
    await screen.findByText(/La apertura tiene un resultado pendiente/)
    expect(screen.queryByRole('button', { name: 'Abrir mi caja' })).not.toBeInTheDocument()
    expect(fake.writes()).toHaveLength(1)
  })
  it('creates once on command, persists exact inputs/expectedVersion, and restores the same draft after reload', async () => {
    const fake = server(); const view = mount(fake); await select(); expect(fake.writes()).toHaveLength(0)
    await createDraft()
    await screen.findByText(/Venta preparada/)
    expect(fake.writes()).toHaveLength(2)
    expect(fake.writes()[1]?.[1]?.body).toContain('"expectedVersion":1')
    expect(fake.writes()[1]?.[1]?.body).toContain('"quantity":2')
    expect(screen.getByText('Total a cobrar: S/ 0.20')).toBeInTheDocument()
    const search = screen.getByTestId('location').textContent!; expect(search).toContain('sale=' + saleId)
    view.unmount(); mount(fake, search)
    await screen.findByText(/Venta recuperada del servidor/)
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeEnabled()
    expect(fake.fetcher.mock.calls.some(([path]) => String(path).endsWith(`/sales/${saleId}/draft`))).toBe(true)
    expect(fake.writes()).toHaveLength(2)
    expect(fake.writes().some(([path]) => /confirm|payments|ticket/.test(String(path)))).toBe(false)
  })
  it('reconciles lost line-save responses on the same ID without resending', async () => {
    const fake = server(); fake.state.loseSave = true; mount(fake); await select(); await createDraft()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText(/Venta preparada y verificada/)
    expect(fake.writes()).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Confirmar venta y registrar pagos' })).toBeDisabled()
  })
  it('allows editing the same recovered UUID when the URL uses uppercase letters', async () => {
    const fake = server(); fake.state.sale = withLines(); mount(fake, '?sale=' + saleId.toUpperCase())
    await screen.findByText(/Venta recuperada del servidor/)
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad en carrito/ }), { target: { value: '3' } })
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeEnabled()
    expect(fake.writes()).toHaveLength(0)
  })
  it('restores both presentations and exact totals, then explicitly persists removal with the recovered version', async () => {
    const fake = server(); const view = mount(fake); await select('2')
    fireEvent.change(screen.getByRole('combobox', { name: /Presentación de/ }), { target: { value: product.units[1]!.id } })
    fireEvent.change(screen.getByRole('textbox', { name: /Cantidad a agregar/ }), { target: { value: '1' } })
    fireEvent.click(screen.getByRole('button', { name: `Seleccionar ${product.name}` }))
    await createDraft()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText('Total a cobrar: S/ 1.20')
    const search = screen.getByTestId('location').textContent!
    view.unmount(); mount(fake, search)
    await screen.findByText(/Venta recuperada del servidor/)
    const cart = within(screen.getByRole('region', { name: 'Carrito local' }))
    expect(cart.getByRole('textbox', { name: /Cantidad en carrito.*Tableta/ })).toHaveValue('2')
    expect(cart.getByRole('textbox', { name: /Cantidad en carrito.*Caja de 10/ })).toHaveValue('1')
    expect(cart.getByText('S/ 1.20')).toBeInTheDocument()
    expect(fake.writes()).toHaveLength(2)
    fireEvent.click(cart.getByRole('button', { name: /Quitar.*Caja de 10/ }))
    expect(fake.writes()).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText('Total a cobrar: S/ 0.20')
    expect(fake.writes()).toHaveLength(3)
    expect(fake.writes()[2]?.[1]?.body).toContain('"expectedVersion":2')
    expect(fake.state.sale?.lines).toHaveLength(1)
  })
  it('blocks another save when reconciliation fails, then recovers by reads only', async () => {
    const fake = server(); fake.state.loseSave = true; fake.state.failReads = true; mount(fake); await select()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText(/No pudimos confirmar el resultado/)
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar venta pendiente' })).toBeEnabled())
    fake.state.failReads = false
    fireEvent.click(screen.getByRole('button', { name: 'Consultar venta pendiente' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Cobrar' })).toBeEnabled())
    expect(decimalLabel(fake.state.sale!.totalAmount, 2)).toBe('0.20')
    expect(fake.writes()).toHaveLength(2)
  })
  it('does not edit a restored draft attached to another cash session or another seller', async () => {
    const fake = server(); fake.state.sale = { ...withLines(), sellerMembershipId: id('90'), cashSessionId: id('91') }
    mount(fake, '?sale=' + saleId)
    await screen.findByText(/Venta recuperada del servidor/)
    expect(screen.getByText(/pertenece a otra membresía/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toBeDisabled()
    expect(fake.writes()).toHaveLength(0)
  })
  it('recovers a known draft on demand without creating a new one', async () => {
    const fake = server(); fake.state.sale = withLines(); mount(fake, '?draftPending=1')
    fireEvent.click(screen.getByText('Recuperación excepcional por identificador'))
    fireEvent.change(screen.getByRole('textbox', { name: 'Identificador de la venta pendiente' }), { target: { value: saleId } })
    fireEvent.click(screen.getByRole('button', { name: 'Recuperar y reemplazar carrito' }))
    await screen.findByText(/El carrito se reemplazó/)
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(fake.writes()).toHaveLength(0)
  })
  it('preserves local edits on version conflict and requires review before using the fresh version', async () => {
    const fake = server(); fake.state.conflict = true; mount(fake); await select()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByRole('button', { name: 'Conservar mi carrito revisado' })
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(screen.getByText('Total consultado en el servidor: S/ 0.30')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Conservar mi carrito revisado' }))
    expect(fake.writes()).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText(/Venta preparada/)
    expect(fake.writes()[2]?.[1]?.body).toContain('"expectedVersion":2')
  })
  it('blocks another creation after loss of the POST response, including after reload', async () => {
    const fake = server(); fake.state.loseCreate = true; const view = mount(fake); await select()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText(/No recibimos el identificador de la venta/)
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    const search = screen.getByTestId('location').textContent!; view.unmount(); mount(fake, search)
    expect(screen.getByRole('searchbox')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: `Seleccionar ${product.name}` })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    expect(fake.writes()).toHaveLength(1)
    expect(fake.state.sale?.saleId).toBe(saleId)
  })
  it('blocks writes after a license denial and retains the cart', async () => {
    const fake = server(); fake.state.denySave = true; mount(fake); await select()
    fireEvent.click(screen.getByRole('button', { name: 'Cobrar' }))
    await screen.findByText(/licencia de esta botica no permite/)
    expect(screen.getByRole('button', { name: 'Cobrar' })).toBeDisabled()
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(fake.writes()).toHaveLength(2)
  })
  it.each(['branch', 'session'] as const)('clears the local cart across a %s change', async type => {
    const fake = server(); const view = mount(fake); await select()
    const next = context()
    if (type === 'branch') { next.branchId = id('6'); next.branch = { ...next.branch, id: next.branchId }; next.access = { ...next.access, branchId: next.branchId } }
    else { next.session = { userId: id('99') }; next.access = { ...next.access, userId: id('99') } }
    view.changeContext(next)
    expect(await screen.findByText('Tu carrito está vacío')).toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toHaveValue('')
    expect(fake.writes()).toHaveLength(0)
  })
  it.each([false, true])('preserves cart during background access checks and applies a returned denial=%s', async denied => {
    const fake = server(); const service = createPreviewService('owner')
    const original = service.access.bind(service)
    const result = await original(tenantId, branchId)
    let release!: (value: typeof result) => void
    const access = vi.spyOn(service, 'access').mockResolvedValueOnce(result).mockImplementation(() => new Promise(resolve => { release = resolve }))
    const view = render(<RuntimeProvider runtime={{ service, http: new HttpClient(fake.fetcher), preview: false }}>
      <MemoryRouter initialEntries={[`/boticas/${tenantId}/sucursales/${branchId}/ventas`]}><AppRoutes /></MemoryRouter></RuntimeProvider>)
    await screen.findByRole('searchbox'); await select()
    act(() => { focusManager.setFocused(false); focusManager.setFocused(true) })
    await waitFor(() => expect(access).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    await act(async () => release(denied ? { isAllowed: false, code: 'access.membership_inactive', context: null } : result))
    if (denied) {
      expect(await screen.findByText('Tu acceso está restringido')).toBeInTheDocument()
      expect(screen.queryByRole('region', { name: 'Carrito local' })).not.toBeInTheDocument()
    } else expect(screen.getByRole('textbox', { name: /Cantidad en carrito/ })).toHaveValue('2')
    expect(fake.writes()).toHaveLength(0)
    view.unmount(); focusManager.setFocused(undefined)
  })
})
