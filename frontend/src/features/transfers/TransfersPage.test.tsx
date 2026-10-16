import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { TransfersPage } from './TransfersPage'
import { allowed, detail, id, json, lot, mountTransfers, n, other, transfer, transferServer, workspace } from './transfers.test.support'

async function open() { fireEvent.click(await screen.findByRole('button', { name: /Ver transferencia Origen unitario/ })); return screen.findByRole('region', { name: 'Detalle de mercadería' }) }
async function request() {
  await waitFor(() => expect(screen.getByRole('combobox', { name: 'Sucursal origen' })).toBeEnabled())
  fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal origen' }), { target: { value: other.id } })
  fireEvent.change(screen.getByRole('textbox', { name: /Buscar producto por nombre/ }), { target: { value: 'sintético' } }); fireEvent.click(screen.getByRole('button', { name: 'Buscar productos' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar ' + detail.product.name })); fireEvent.click(await screen.findByRole('button', { name: 'Usar producto consultado' }))
  fireEvent.change(screen.getByRole('combobox', { name: 'Presentación solicitada' }), { target: { value: detail.units[0]!.id } }); fireEvent.change(screen.getByRole('textbox', { name: 'Cantidad solicitada' }), { target: { value: '2' } })
  fireEvent.click(screen.getByRole('button', { name: 'Agregar producto a la solicitud' })); fireEvent.click(screen.getByRole('checkbox', { name: /Revisé origen, destino/ }))
  const send = screen.getByRole('button', { name: 'Enviar solicitud de mercadería' }); fireEvent.click(send); fireEvent.click(send)
}
it('requests, approves, allocates multiple origin lots and receives actual shortages through explicit reviews and rereads', async () => {
  const fake = transferServer(); fake.state.lots = [{ ...lot, quantityAvailableBase: n('1.5') }, { ...lot, inventoryLotId: id(31), batchNumber: 'BATCH-UNIT-2', quantityAvailableBase: n('5') }]
  mountTransfers(fake, <TransfersPage />); await request()
  await screen.findByRole('heading', { name: 'Transferencia: Solicitada' }); expect(fake.writes()).toHaveLength(1)
  expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled()
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })).toBeEnabled()); fireEvent.click(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar aprobación' }))
  await screen.findByRole('heading', { name: 'Transferencia: Aprobada' }); const first = await screen.findByRole('button', { name: 'Elegir lote BATCH-UNIT' }); fireEvent.click(first)
  fireEvent.change(screen.getByRole('textbox', { name: 'Cantidad base del lote' }), { target: { value: '1.5' } }); fireEvent.click(screen.getByRole('button', { name: 'Agregar asignación de lote' }))
  fireEvent.click(screen.getByRole('button', { name: 'Elegir lote BATCH-UNIT-2' })); fireEvent.change(screen.getByRole('textbox', { name: 'Cantidad base del lote' }), { target: { value: '0.5' } }); fireEvent.click(screen.getByRole('button', { name: 'Agregar asignación de lote' }))
  expect(screen.getByRole('button', { name: 'Confirmar despacho de mercadería' })).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé el despacho completo/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar despacho de mercadería' }))
  await screen.findByRole('heading', { name: 'Transferencia: EN TRÁNSITO' })
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Recibido base de asignación 1' })).toBeEnabled())
  fireEvent.change(screen.getByRole('textbox', { name: 'Recibido base de asignación 1' }), { target: { value: '1.25' } }); fireEvent.change(screen.getByRole('textbox', { name: 'Recibido base de asignación 2' }), { target: { value: '0' } })
  expect(screen.getByText(/Diferencia 0.75 base/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('checkbox', { name: /físicamente recibidas/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción de mercadería' }))
  await screen.findByRole('heading', { name: 'Transferencia: RECIBIDA' }); expect(fake.writes()).toHaveLength(4)
  expect(fake.writes()[2]?.[1]?.body).toContain('"quantityBase":1.5'); expect(fake.writes()[3]?.[1]?.body).toContain('"quantityBase":0')
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes(`/branches/${other.id}/inventory/lots?`))).toBe(true)
  expect(fake.fetcher.mock.calls.filter(([path, init]) => init?.method === 'GET' && String(path).endsWith('/transfers/' + transfer.id)).length).toBeGreaterThanOrEqual(4)
  expect(screen.getByTestId('location')).toHaveTextContent('goodsId='); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('goodsPending'))
})
it('paginates and filters on the server, preserves details on reload and treats errors differently from empty results', async () => {
  const fake = transferServer(); fake.state.records = Array.from({ length: 21 }, (_, index) => ({ ...transfer, id: id(200 + index) })); const view = mountTransfers(fake, <TransfersPage />)
  const next = screen.getByRole('button', { name: 'Página siguiente' }); await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next)
  await waitFor(() => expect(screen.getAllByRole('button', { name: /Ver transferencia/ })).toHaveLength(1)); expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('offset=20&limit=20'))).toBe(true)
  fireEvent.change(screen.getByRole('combobox', { name: 'Estado de transferencia' }), { target: { value: 'received' } }); await screen.findByText('No hay transferencias en esta página y filtro.')
  view.unmount(); fake.state.records = [transfer]; const second = mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id)
  await screen.findByRole('heading', { name: 'Transferencia: Solicitada' }); second.unmount(); fake.state.failList = true; mountTransfers(fake, <TransfersPage />)
  await screen.findByText('No pudimos consultar transferencias'); expect(screen.queryByText('No hay transferencias en esta página y filtro.')).not.toBeInTheDocument()
})
it('does not infer source authority from destination, and keeps cashier approval, dispatch and receipt blocked', async () => {
  const fake = transferServer(); fake.state.records = [transfer]; fake.state.access.set(other.id, { isAllowed: false, code: 'access.branch_not_assigned', context: null })
  const view = mountTransfers(fake, <TransfersPage />); await open(); expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes(`/access?branchId=${other.id}`))).toBe(true)
  view.unmount(); const cashier = { ...workspace, access: { ...workspace.access, role: 'cashier' as const } }; fake.state.access.set(other.id, allowed(other.id, 'cashier')); fake.state.access.set(workspace.branchId, allowed(workspace.branchId, 'cashier'))
  mountTransfers(fake, <TransfersPage />, '', cashier); await open()
  expect(screen.getByRole('combobox', { name: 'Sucursal de consulta' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled()
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Motivo de cancelación' })).toBeEnabled()); expect(fake.writes()).toHaveLength(0)
})
it('cancels before dispatch only with a required reason, confirmation and persistent event', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, events: [...transfer.events] }]; mountTransfers(fake, <TransfersPage />); await open()
  await waitFor(() => expect(screen.getByRole('textbox', { name: 'Motivo de cancelación' })).toBeEnabled())
  fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo cancelar/ })); expect(screen.getByRole('button', { name: 'Confirmar cancelación' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Motivo de cancelación' }), { target: { value: ' Motivo unitario ' } }); fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo cancelar/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar cancelación' }))
  await screen.findByRole('heading', { name: 'Transferencia: CANCELADA' }); expect(fake.writes()[0]?.[1]?.body).toBe('{"reason":"Motivo unitario"}'); expect(screen.getByText(/Motivo: Motivo unitario/)).toBeInTheDocument()
})
it('preserves a lost create without ID across reload, never treats a matching list record as idempotency', async () => {
  const fake = transferServer(); fake.state.lose = 'create'; const view = mountTransfers(fake, <TransfersPage />); await request()
  await screen.findByText(/Hay una operación pendiente de conciliación/); await waitFor(() => expect(fake.writes()).toHaveLength(1))
  fireEvent.click(screen.getByRole('button', { name: 'Actualizar consulta' }))
  await waitFor(() => expect(screen.getByRole('button', { name: /Ver transferencia/ })).toBeEnabled()); fireEvent.click(screen.getByRole('button', { name: /Ver transferencia/ })); await screen.findByRole('heading', { name: 'Transferencia: Solicitada' })
  expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled(); expect(screen.getByRole('button', { name: 'Continuar tras conciliación de creación' })).toBeDisabled()
  const search = screen.getByTestId('location').textContent!; expect(search).toContain('goodsUnidentified=1'); view.unmount(); mountTransfers(fake, <TransfersPage />, search)
  await screen.findByRole('heading', { name: 'Transferencia: Solicitada' }); expect(screen.getByRole('button', { name: 'Preparar otra solicitud de mercadería' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('does not clear an uncertain approval from an early requested read, then reconciles the same persisted event', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, events: [...transfer.events] }]; fake.state.lose = 'approve'; fake.state.hold = true
  mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id); await screen.findByRole('heading', { name: 'Transferencia: Solicitada' })
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })).toBeEnabled()); fireEvent.click(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar aprobación' }))
  const consult = await screen.findByRole('button', { name: 'Consultar la misma transferencia' }); await waitFor(() => expect(consult).toBeEnabled()); fireEvent.click(consult)
  await screen.findByRole('heading', { name: 'Transferencia: Solicitada' }); expect(screen.getByTestId('location')).toHaveTextContent('goodsPending=approve'); expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled()
  fake.state.records[0] = { ...transfer, status: 'approved', events: [...transfer.events, { ...transfer.events[0]!, id: id(64), eventType: 'approved' }] }
  fireEvent.click(consult); await screen.findByRole('heading', { name: 'Transferencia: Aprobada' }); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('goodsPending')); expect(fake.writes()).toHaveLength(1)
})
it.each([400, 401, 403, 409, 429, 503])('shows HTTP %i without automatic write retries', async status => {
  const fake = transferServer(); fake.state.records = [transfer]; fake.state.reject = status; mountTransfers(fake, <TransfersPage />); await open()
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })).toBeEnabled()); fireEvent.click(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar aprobación' }))
  await screen.findByText('No pudimos completar o consultar la transferencia'); await waitFor(() => expect(fake.writes()).toHaveLength(1))
  if (status >= 500 || status === 401 || status === 403) expect(screen.getByRole('button', { name: 'Preparar otra solicitud de mercadería' })).toBeDisabled()
})
it('cancels reads when switching workspace and discards a late transfer response from the previous branch', async () => {
  const fake = transferServer(); fake.state.records = [transfer]; let resolve!: (value: Response) => void; fake.state.delayRead = new Promise<Response>(done => { resolve = done })
  const view = mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id)
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).endsWith('/transfers/' + transfer.id))).toBe(true))
  const signal = fake.fetcher.mock.calls.find(([path]) => String(path).endsWith('/transfers/' + transfer.id))![1]!.signal!
  fake.state.delayRead = null; view.changeContext({ ...workspace, branchId: other.id, branch: other, base: '/new', access: { ...workspace.access, branchId: other.id } })
  expect(signal.aborted).toBe(true); await act(async () => resolve(json({ ...transfer, destinationBranchName: 'Old late value' })))
  expect(screen.queryByText(/Old late value/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})

function CacheProbe({ capture }: { capture: (client: QueryClient) => void }) { const client = useQueryClient(); useEffect(() => capture(client), [capture, client]); return null }
it('invalidates only stock, lots and search of the actual two endpoints after reading a received transfer', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, status: 'received' }]; let client!: QueryClient
  mountTransfers(fake, <><CacheProbe capture={value => { client = value }} /><TransfersPage /></>)
  await screen.findByRole('button', { name: /Ver transferencia/ })
  const source = ['tenant', workspace.tenantId, other.id, 'stock']; const destination = ['tenant', workspace.tenantId, workspace.branchId, 'inventory-lots']; const search = ['tenant', workspace.tenantId, workspace.branchId, 'product-search']
  const unrelated = ['tenant', workspace.tenantId, id(999), 'stock']; const foreign = ['tenant', id(999), workspace.branchId, 'stock']
  for (const key of [source, destination, search, unrelated, foreign]) client.setQueryData(key, [])
  await open(); await waitFor(() => expect(client.getQueryState(source)?.isInvalidated).toBe(true))
  expect(client.getQueryState(destination)?.isInvalidated).toBe(true); expect(client.getQueryState(search)?.isInvalidated).toBe(true)
  expect(client.getQueryState(unrelated)?.isInvalidated).toBe(false); expect(client.getQueryState(foreign)?.isInvalidated).toBe(false)
})
it('preserves a pending write on context departure and ignores its late response without creating another transfer', async () => {
  const fake = transferServer(); fake.state.records = [transfer]; const base = fake.fetcher.getMockImplementation()!; let resolve!: (value: Response) => void
  fake.fetcher.mockImplementation((path, init) => init?.method === 'POST' && String(path).endsWith('/approve') ? new Promise<Response>(done => { resolve = done }) : base(path, init))
  const view = mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id); await screen.findByRole('heading', { name: 'Transferencia: Solicitada' })
  await waitFor(() => expect(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })).toBeEnabled()); fireEvent.click(screen.getByRole('checkbox', { name: /autorizo su aprobación/ })); fireEvent.click(screen.getByRole('button', { name: 'Confirmar aprobación' }))
  await waitFor(() => expect(fake.writes()).toHaveLength(1)); const signal = fake.writes()[0]![1]!.signal!; const search = screen.getByTestId('location').textContent!
  expect(search).toContain('goodsPending=approve'); view.unmount(); expect(signal.aborted).toBe(true)
  await act(async () => resolve(json({ ...transfer, status: 'approved' })))
  mountTransfers(fake, <TransfersPage />, search); await screen.findByRole('heading', { name: 'Transferencia: Solicitada' })
  expect(screen.getByRole('button', { name: 'Confirmar aprobación' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('blocks dispatch and receipt in invalid states and when source/destination authorization is denied', async () => {
  const fake = transferServer(); fake.state.records = [{ ...transfer, status: 'approved' }]; fake.state.access.set(other.id, { isAllowed: false, code: 'access.outside_schedule', context: null })
  const view = mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id); await screen.findByText(/Despacho no disponible/)
  expect(screen.queryByRole('button', { name: 'Confirmar despacho de mercadería' })).not.toBeInTheDocument(); expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/inventory/lots?'))).toBe(false)
  view.unmount(); fake.state.records = [{ ...transfer, status: 'in_transit' }]; fake.state.access.set(workspace.branchId, { isAllowed: false, code: 'access.license_suspended', context: null })
  mountTransfers(fake, <TransfersPage />, '?goodsId=' + transfer.id); await screen.findByText(/Recepción no disponible/)
  expect(screen.queryByRole('button', { name: 'Confirmar recepción de mercadería' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Confirmar cancelación' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
