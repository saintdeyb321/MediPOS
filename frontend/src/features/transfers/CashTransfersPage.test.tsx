import { fireEvent, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { CashTransfersPage } from './CashTransfersPage'
import { cashServer, sentCash } from './cash.test.support'
import { id, mountTransfers, other, workspace } from './transfers.test.support'

async function selectCash() {
  await screen.findAllByRole('option', { name: /apertura UTC/ }); fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(302) } }); await screen.findByText(/Caja ABIERTA/)
}
async function dispatch() {
  await selectCash(); fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } }); fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1.0001' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé caja origen/ })); const button = screen.getByRole('button', { name: 'Confirmar despacho de sencillo' }); fireEvent.click(button); fireEvent.click(button)
}
it('dispatches once from a selected open cash with exact amount and rereads without changing ledger locally', async () => {
  const fake = cashServer(); mountTransfers(fake, <CashTransfersPage />); await dispatch()
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]?.[1]?.body).toContain('"amount":1.0001'); expect(fake.cash.expected.literal).toBe('10')
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferPending'))
  expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled()
  expect(fake.fetcher.mock.calls.some(([path, init]) => init?.method === 'GET' && String(path).endsWith('/cash-transfers/' + sentCash.id))).toBe(true)
})
it('receives the entire pending amount once into another open destination session and verifies terminal state', async () => {
  const fake = cashServer(); fake.cash.records = [sentCash]; mountTransfers(fake, <CashTransfersPage />); fireEvent.click(await screen.findByRole('button', { name: 'Consultar sencillo de ' + other.name }))
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled(); await selectCash()
  fireEvent.click(screen.getByRole('checkbox', { name: /Comprobé el efectivo recibido/ })); const button = screen.getByRole('button', { name: 'Confirmar recepción de sencillo' }); fireEvent.click(button); fireEvent.click(button)
  await screen.findByRole('heading', { name: 'Sencillo: RECIBIDO' }); expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toBe(`{"destinationCashSessionId":"${id(302)}"}`)
  expect(screen.queryByRole('textbox', { name: /cantidad recibida/i })).not.toBeInTheDocument()
})
it('denies Pharmacist before all cash requests and limits Cashier to its own session', async () => {
  const fake = cashServer(); const view = mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'pharmacist' } })
  expect(screen.getByText(/Pharmacist no tiene este permiso/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled(); view.unmount()
  mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } }); await selectCash()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/cash-sessions/active'))).toBe(false); expect(screen.getByRole('combobox', { name: 'Destino de consulta' })).toBeDisabled()
})
it('does not open cash automatically and blocks zero, excessive, out-of-range and overprecision amounts', async () => {
  const fake = cashServer(); fake.cash.open = false; const view = mountTransfers(fake, <CashTransfersPage />)
  await screen.findByRole('link', { name: /Ir a Caja/ }); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
  view.unmount(); fake.cash.open = true; mountTransfers(fake, <CashTransfersPage />); await selectCash(); fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } })
  for (const value of ['0', '10.0001', '0.00001', '100000000000000']) { fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value } }); fireEvent.click(screen.getByRole('checkbox', { name: /Revisé caja origen/ })); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled() }
  expect(fake.writes()).toHaveLength(0)
})
it('requires a new review after changing the cash session and rejects a foreign-membership cashier session', async () => {
  const fake = cashServer(); fake.cash.sessions.push(id(303)); const view = mountTransfers(fake, <CashTransfersPage />); await selectCash()
  fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal destino de sencillo' }), { target: { value: other.id } }); fireEvent.change(screen.getByRole('textbox', { name: 'Importe de sencillo PEN' }), { target: { value: '1' } }); fireEvent.click(screen.getByRole('checkbox', { name: /Revisé caja origen/ }))
  expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeEnabled(); fireEvent.change(screen.getByRole('combobox', { name: 'Caja para sencillo' }), { target: { value: id(303) } })
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /Revisé caja origen/ })).not.toBeChecked()); expect(fake.writes()).toHaveLength(0)
  view.unmount(); fake.cash.membershipId = id(999); mountTransfers(fake, <CashTransfersPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } })
  await screen.findByText('No pudimos consultar cajas'); expect(screen.getByRole('button', { name: 'Confirmar despacho de sencillo' })).toBeDisabled()
})
it('preserves uncertain dispatch across reload, even after a matching pending record is consulted', async () => {
  const fake = cashServer(); fake.cash.lose = 'create'; const view = mountTransfers(fake, <CashTransfersPage />); await dispatch()
  await screen.findByText(/Hay una operación pendiente/); await waitFor(() => expect(fake.writes()).toHaveLength(1))
  fireEvent.change(screen.getByRole('combobox', { name: 'Destino de consulta' }), { target: { value: '' } }); fireEvent.click(await screen.findByRole('button', { name: 'Consultar sencillo de ' + workspace.branch.name }))
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(screen.getByRole('button', { name: 'Preparar otro despacho de sencillo' })).toBeDisabled()
  const search = screen.getByTestId('location').textContent!; view.unmount(); mountTransfers(fake, <CashTransfersPage />, search)
  await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); expect(screen.getByTestId('location')).toHaveTextContent('cashTransferUnidentified=1'); expect(fake.writes()).toHaveLength(1)
})
it('keeps an early in-transit read pending after lost receipt, then reconciles received without another POST', async () => {
  const fake = cashServer(); fake.cash.records = [sentCash]; fake.cash.lose = 'receive'; fake.cash.hold = true
  mountTransfers(fake, <CashTransfersPage />, '?cashTransferId=' + sentCash.id); await screen.findByRole('heading', { name: 'Sencillo: EN TRÁNSITO' }); await selectCash()
  fireEvent.click(screen.getByRole('checkbox', { name: /Comprobé el efectivo recibido/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' }))
  const consult = await screen.findByRole('button', { name: 'Consultar la misma transferencia' }); await waitFor(() => expect(consult).toBeEnabled())
  expect(screen.getByTestId('location')).toHaveTextContent('cashTransferPending=receive'); expect(screen.getByRole('button', { name: 'Confirmar recepción de sencillo' })).toBeDisabled()
  fake.cash.records = [{ ...sentCash, status: 'received', destinationCashSessionId: id(302), receivedAt: sentCash.dispatchedAt, receivedByActorId: workspace.session.userId }]; fireEvent.click(consult)
  await screen.findByRole('heading', { name: 'Sencillo: RECIBIDO' }); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('cashTransferPending')); expect(fake.writes()).toHaveLength(1)
})
it('uses hasMore pagination, distinguishes empty and failed pending queries, and never invents history endpoints', async () => {
  const fake = cashServer(); fake.cash.records = Array.from({ length: 21 }, (_, index) => ({ ...sentCash, id: id(400 + index) })); const view = mountTransfers(fake, <CashTransfersPage />)
  const next = screen.getAllByRole('button', { name: 'Página siguiente' })[0]!; await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next)
  await waitFor(() => expect(screen.getAllByRole('button', { name: /Consultar sencillo de/ })).toHaveLength(1)); expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/cash-transfers/pending?offset=20'))).toBe(true)
  view.unmount(); fake.cash.records = []; const empty = mountTransfers(fake, <CashTransfersPage />); await screen.findByText(/No hay sencillo pendiente/); empty.unmount(); fake.cash.fail = true; mountTransfers(fake, <CashTransfersPage />)
  await screen.findByText('No pudimos consultar sencillo pendiente'); expect(screen.queryByText(/No hay sencillo pendiente/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it.each([403, 409, 429, 503])('handles HTTP %i without repeating dispatch', async status => {
  const fake = cashServer(); fake.cash.reject = status; mountTransfers(fake, <CashTransfersPage />); await dispatch(); await screen.findByText('No pudimos completar o consultar el sencillo')
  expect(fake.writes()).toHaveLength(1)
  if (status === 503) expect(screen.getByTestId('location')).toHaveTextContent('cashTransferUnidentified=1')
})
