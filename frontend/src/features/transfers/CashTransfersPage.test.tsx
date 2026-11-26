import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it } from 'vitest'
import { stringifyExactJson } from '../../shared/api/exactJson'
import { CashTransfersPage } from './CashTransfersPage'
import { cashServer, sentCash } from './cash.test.support'
import { id, json, mountTransfers, n, other, workspace } from './transfers.test.support'

beforeEach(() => sessionStorage.clear())

function openDispatch() { fireEvent.click(screen.getByRole('button', { name: 'Nuevo envío de sencillo' })) }
async function selectCash() {
  await screen.findAllByRole('option', { name: /apertura UTC/ }); fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(302) } }); await screen.findByText('Caja abierta.')
}
async function dispatch() {
  openDispatch()
  await selectCash(); fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } }); fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1.0001' } })
  const button = screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })
  expect(button).toBeEnabled()
  expect(screen.getByText(/Entrega S\/ 1\.0001/)).toBeInTheDocument(); fireEvent.click(button); fireEvent.click(button)
}
it('opens the cash selector only in the editor and resumes the same unsent amount', async () => {
  const fake = cashServer(); mountTransfers(fake, <CashTransfersPage />)
  await screen.findByText('No hay sencillo pendiente en esta página y filtro.')
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/cash-sessions/active'))).toBe(false)
  openDispatch()
  fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1.0001' } })
  fireEvent.click(screen.getByRole('button', { name: 'Volver a transferencias' }))
  expect(screen.queryByRole('textbox', { name: 'Importe de sencillo PEN' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Retomar envío de sencillo' }))
  expect(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' })).toHaveValue('1.0001')
  expect(fake.writes()).toHaveLength(0)
})
it('dispatches once from a selected open cash with exact amount and rereads without changing ledger locally', async () => {
  const fake = cashServer(); mountTransfers(fake, <CashTransfersPage />); await dispatch()
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"amount":1.0001')
  expect(new Headers(fake.writes()[0]?.[1]?.headers).get('Idempotency-Key')).toMatch(/^[a-f\d-]{36}$/)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferPending'))
  expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled()
  expect(fake.fetcher.mock.calls.some(([path, init]) => init?.method === 'GET' && String(path).endsWith('/cash-transfers/' + sentCash.id))).toBe(true)
})
it('receives the entire pending amount once into another open destination session and verifies terminal state', async () => {
  const fake = cashServer(); fake.cash.records = [sentCash]; mountTransfers(fake, <CashTransfersPage />); fireEvent.click(await screen.findByRole('button', { name: 'Consultar sencillo de ' + other.name }))
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled(); await selectCash()
  const button = screen.getByRole('button', { name: 'Confirmar recepción de sencillo' }); fireEvent.click(button); fireEvent.click(button)
  await screen.findByRole('heading', { name: 'Sencillo: RECIBIDO' }); expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toBe(`{"destinationCashSessionId":"${id(302)}"}`)
  expect(screen.queryByRole('textbox', { name: /cantidad recibida/i })).not.toBeInTheDocument()
})
it('denies Pharmacist before all cash requests and limits Cashier to its own session', async () => {
  const fake = cashServer(); const view = mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'pharmacist' } })
  expect(screen.getByText(/Pharmacist no tiene este permiso/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled(); view.unmount()
  mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } }); openDispatch(); await selectCash()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/cash-sessions/active'))).toBe(false); expect(screen.queryByRole('combobox', { name: 'Destino de consulta' })).not.toBeInTheDocument()
})
it('does not open cash automatically and blocks zero, negative, out-of-range and overprecision amounts', async () => {
  const fake = cashServer(); fake.cash.open = false; const view = mountTransfers(fake, <CashTransfersPage />)
  openDispatch()
  await screen.findByRole('link', { name: /Ir a Caja/ }); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
  view.unmount(); fake.cash.open = true; mountTransfers(fake, <CashTransfersPage />); openDispatch(); await selectCash(); fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } })
  for (const value of ['0', '-1', '0.00001', '100000000000000']) { fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value } }); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled() }
  expect(fake.writes()).toHaveLength(0)
})
it('lets the server reject insufficient funds and keeps the original inputs editable without resending', async () => {
  const fake = cashServer(); fake.cash.available = n('10')
  mountTransfers(fake, <CashTransfersPage />); openDispatch(); await selectCash()
  const destination = screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' })
  expect(Array.from(destination.querySelectorAll('option')).map(option => option.value)).not.toContain(workspace.branchId)
  const amount = screen.getByRole('textbox', { name: 'Importe de sencillo PEN' })
  const button = screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })
  fireEvent.change(amount, { target: { value: '10.0001' } })
  expect(button).toBeDisabled()
  fireEvent.change(destination, { target: { value: other.id } })
  expect(button).toBeEnabled()
  fireEvent.click(button)
  await screen.findByText(/El efectivo disponible en la caja no alcanza para este envío/)
  expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"amount":10.0001')
  expect(fake.cash.records).toHaveLength(0)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferRequest'))
  expect(destination).toHaveValue(other.id); expect(destination).toBeEnabled()
  expect(amount).toHaveValue('10.0001'); expect(amount).toBeEnabled()
})
it.each(['closed', 'unavailable', 'other-branch'])('blocks dispatch while reading cash status and when it returns %s', async state => {
  const fake = cashServer()
  const normal = fake.fetcher.getMockImplementation()!
  let reply!: (response: Response) => void
  const status = new Promise<Response>(resolve => { reply = resolve })
  fake.fetcher.mockImplementation((path, init) => String(path).endsWith('/cash-sessions/' + id(302)) ? status : normal(path, init))
  mountTransfers(fake, <CashTransfersPage />); openDispatch()
  await screen.findAllByRole('option', { name: /apertura UTC/ })
  fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(302) } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1' } })
  await screen.findByText('Consultando estado de caja…')
  const button = screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })
  expect(button).toBeDisabled()
  reply(state === 'unavailable' ? json({ code: 'unit.unavailable' }, 503) : json({
    cashSessionId: id(302), branchId: state === 'other-branch' ? other.id : workspace.branchId,
    membershipId: workspace.access.membershipId, openingAmount: 10, openedAt: sentCash.dispatchedAt,
    closedAt: state === 'closed' ? sentCash.dispatchedAt : null, status: state === 'closed' ? 'closed' : 'open', expectedCashAmount: null,
  }))
  await screen.findByText(state === 'closed' ? /La caja consultada ya está cerrada/ : 'No pudimos consultar estado de caja')
  expect(button).toBeDisabled()
  expect(fake.writes()).toHaveLength(0)
})
it('shows the current commitment after changing the cash session and rejects a foreign-membership cashier session', async () => {
  const fake = cashServer(); fake.cash.sessions.push(id(303)); const view = mountTransfers(fake, <CashTransfersPage />); openDispatch(); await selectCash()
  fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } }); fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1' } })
  expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeEnabled(); fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(303) } })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeEnabled())
  expect(screen.getByRole('combobox', { name: 'Caja para sencillo' })).toHaveValue(id(303)); expect(fake.writes()).toHaveLength(0)
  view.unmount(); fake.cash.membershipId = id(999); mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } })
  openDispatch()
  await screen.findByText('No pudimos consultar cajas'); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled()
})
it('preserves the legacy guard when there is no original intention key', async () => {
  const fake = cashServer(); fake.cash.records = [{ ...sentCash, sourceBranchId: workspace.branchId, destinationBranchId: other.id }]
  mountTransfers(fake, <CashTransfersPage />, '?cashTransferPending=create&cashTransferUnidentified=1')
  fireEvent.change(screen.getByRole('combobox', { name: 'Destino de consulta' }), { target: { value: '' } })
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar sencillo de ' + workspace.branch.name }))
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' })
  expect(screen.getByRole('button', { name: 'Preparar otro despacho de sencillo' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación de creación' })).toBeDisabled()
  expect(fake.writes()).toHaveLength(0)
})
it('recovers a lost dispatch after reload with the original source receipt and never repeats the POST', async () => {
  const fake = cashServer(); fake.cash.lose = 'create'; const view = mountTransfers(fake, <CashTransfersPage />); await dispatch()
  await screen.findByText('No pudimos completar o consultar el sencillo')
  const key = new Headers(fake.writes()[0]?.[1]?.headers).get('Idempotency-Key')
  expect(key).toMatch(/^[a-f\d-]{36}$/); expect(screen.getByTestId('location')).toHaveTextContent(`cashTransferRequest=${key}`)
  const normal = fake.fetcher.getMockImplementation()!
  let receipt = 'missing'
  fake.fetcher.mockImplementation((path, init) => {
    if (!String(path).includes('/operations/')) return normal(path, init)
    expect(String(path).endsWith(`/branches/${workspace.branchId}/operations/${key}`)).toBe(true)
    if (receipt === 'missing') return Promise.resolve(json({ code: 'operation.not_observed' }, 404))
    const original = fake.cash.records[0]!
    const result = receipt === 'foreign' ? { ...original, dispatchedByActorId: id(999) } : original
    return Promise.resolve(json({ requestId: key, kind: 'cash-transfer-dispatch', resultJson: stringifyExactJson(result) }))
  })
  view.unmount(); mountTransfers(fake, <CashTransfersPage />)
  expect(screen.queryByRole('combobox', { name: 'Destino de consulta' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Consultar sencillo de ' + workspace.branch.name })).not.toBeInTheDocument()
  const recover = screen.getByRole('button', { name: 'Consultar constancia de la solicitud' })
  fireEvent.click(recover); await screen.findByText(/Aún no se observa una constancia/)
  receipt = 'foreign'; fireEvent.click(recover); await screen.findByText(/El servidor no devolvió una respuesta/)
  expect(screen.queryByRole('button', { name: 'Nuevo envío de sencillo' })).not.toBeInTheDocument()
  receipt = 'committed'; fireEvent.click(recover)
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' })
  expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferRequest')
  expect(fake.writes()).toHaveLength(1)
})
it('keeps an early in-transit read pending after lost receipt, then reconciles received without another POST', async () => {
  const fake = cashServer(); fake.cash.records = [sentCash]; fake.cash.lose = 'receive'; fake.cash.hold = true
  mountTransfers(fake, <CashTransfersPage />, '?cashTransferId=' + sentCash.id); await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); await selectCash()
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' }))
  const consult = await screen.findByRole('button', { name: 'Consultar la misma transferencia' }); await waitFor(() => expect(consult).toBeEnabled())
  expect(screen.getByTestId('location')).toHaveTextContent('cashTransferPending=receive'); expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled()
  fake.cash.records = [{ ...sentCash, status: 'received', destinationCashSessionId: id(302), receivedAt: sentCash.dispatchedAt, receivedByActorId: workspace.session.userId }]; fireEvent.click(consult)
  await screen.findByRole('heading', { name: 'Sencillo: RECIBIDO' }); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferPending')); expect(fake.writes()).toHaveLength(1)
})
it('uses hasMore pagination, distinguishes empty and failed pending queries, and never invents history endpoints', async () => {
  const fake = cashServer(); fake.cash.records = Array.from({ length: 21 }, (_, index) => ({ ...sentCash, id: id(400 + index) })); const view = mountTransfers(fake, <CashTransfersPage />)
  const next = await screen.findByRole('button', { name: 'Página siguiente' }); await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next)
  await waitFor(() => expect(screen.getAllByRole('button', { name: /Consultar sencillo de/ })).toHaveLength(1)); expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/cash-transfers/pending?offset=20'))).toBe(true)
  view.unmount(); fake.cash.records = []; const empty = mountTransfers(fake, <CashTransfersPage />); await screen.findByText(/No hay sencillo pendiente/); empty.unmount(); fake.cash.fail = true; mountTransfers(fake, <CashTransfersPage />)
  await screen.findByText('No pudimos consultar sencillo pendiente'); expect(screen.queryByText(/No hay sencillo pendiente/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it.each([403, 409, 429, 503])('handles HTTP %i without repeating dispatch', async status => {
  const fake = cashServer(); fake.cash.reject = status; mountTransfers(fake, <CashTransfersPage />); await dispatch(); await screen.findByText('No pudimos completar o consultar el sencillo')
  expect(fake.writes()).toHaveLength(1)
  if (status === 503) expect(screen.getByTestId('location')).toHaveTextContent('cashTransferRequest=')
})
