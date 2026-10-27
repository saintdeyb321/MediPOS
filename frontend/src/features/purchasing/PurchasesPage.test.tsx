import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { expect, it } from 'vitest'
import { scopedKey } from '../../shared/api/contracts'
import { id, json, mountOperation, operationServer, purchase, storedLine, time, workspace } from '../../test/operations'
import { PurchasesPage } from './PurchasesPage'

async function begin(fake: ReturnType<typeof operationServer>) {
  fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  if (!screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia documental (opcional)' }), { target: { value: 'REF-UNIT' } })
  if (screen.getByRole('button', { name: 'Nueva recepción' }).getAttribute('disabled') === null && !screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar con productos y lotes' }))
  await waitFor(() => expect(fake.state.purchases).toHaveLength(1))
  return screen.findByRole('region', { name: 'Productos y lotes' })
}
async function pickProduct(region = screen) {
  fireEvent.change(region.getByRole('textbox', { name: 'Buscar producto por nombre, ingrediente o código' }), { target: { value: 'ingrediente' } })
  fireEvent.click(region.getByRole('button', { name: 'Buscar productos' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar Producto sintético unitario' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Usar producto consultado' }))
}
async function addMedicineLine() {
  await pickProduct()
  fireEvent.change(screen.getByRole('combobox', { name: 'Presentación de compra' }), { target: { value: id(12) } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Cantidad de presentaciones' }), { target: { value: '2' } })
  fireEvent.change(screen.getByRole('textbox', { name: 'Costo por presentación (S/)' }), { target: { value: '5.0000' } })
  expect(screen.getByRole('button', { name: 'Agregar línea a la recepción' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Número de lote' }), { target: { value: 'BATCH-UNIT' } })
  fireEvent.change(screen.getByLabelText('Vencimiento del lote'), { target: { value: '2027-12-31' } })
  expect(screen.getByText('Cantidad base exacta: 20')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Agregar línea a la recepción' }))
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé todas las líneas/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Guardar líneas y revisar recepción' }))
  await waitFor(() => expect(screen.queryByRole('region', { name: 'Productos y lotes' })).not.toBeInTheDocument())
}
function loaded(fake: ReturnType<typeof operationServer>) { fake.state.purchases = [{ ...purchase }]; fake.state.lines = [{ ...storedLine }]; return `?purchase=${id(20)}` }
function CacheProbe({ capture }: { capture: (client: QueryClient) => void }) { const client = useQueryClient(); useEffect(() => capture(client), [capture, client]); return null }
it('prepares an explicit branch receipt, saves exact medicine lines, reviews and confirms only once then re-reads', async () => {
  const fake = operationServer(); mountOperation(fake, <PurchasesPage />)
  expect(fake.writes()).toHaveLength(0)
  await begin(fake); await addMedicineLine()
  expect(fake.state.lines[0]).toMatchObject({ batchNumber: 'BATCH-UNIT', expirationDate: '2027-12-31' })
  const review = within(screen.getByRole('region', { name: 'Recepción persistida' }))
  expect(review.getByText(/Importe documental calculado desde las líneas guardadas: S\/ 10.00/)).toBeInTheDocument()
  const confirm = review.getByRole('button', { name: 'Confirmar recepción una vez' }); expect(confirm).toBeDisabled()
  fireEvent.click(review.getByRole('checkbox', { name: /recibí físicamente/ })); fireEvent.click(confirm); fireEvent.click(confirm)
  expect(await screen.findByRole('heading', { name: 'MERCADERÍA REGISTRADA' })).toBeInTheDocument()
  expect(fake.writes().map(([path]) => String(path).split('/').pop())).toEqual(['purchases', 'lines', 'confirm'])
  expect(fake.fetcher.mock.calls.filter(([path, init]) => init?.method === 'GET' && String(path).endsWith('/' + id(20))).length).toBeGreaterThanOrEqual(3)
  expect(screen.queryByRole('button', { name: 'Confirmar recepción una vez' })).not.toBeInTheDocument()
})
it('restores the addressed receipt after reload without any automatic write', async () => {
  const fake = operationServer(); mountOperation(fake, <PurchasesPage />, loaded(fake))
  const review = within(await screen.findByRole('region', { name: 'Recepción persistida' }))
  expect(await review.findByText(/Producto sintético unitario/)).toBeInTheDocument()
  expect(review.getByText('BATCH-UNIT / 2027-12-31')).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('rejects confirmation under the server precondition if lines changed after review, without retry', async () => {
  const fake = operationServer(); mountOperation(fake, <PurchasesPage />, loaded(fake))
  const checkbox = await screen.findByRole('checkbox', { name: /recibí físicamente/ }); await waitFor(() => expect(checkbox).toBeEnabled()); fireEvent.click(checkbox)
  fake.state.lines = [{ ...storedLine, id: id(98), quantity: storedLine.baseQuantity }]
  fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción una vez' }))
  await screen.findByText('No pudimos completar la operación de compra')
  expect(fake.writes()).toHaveLength(1); expect(fake.state.purchases[0]?.status).toBe('draft'); expect(screen.getByRole('button', { name: 'Confirmar recepción una vez' })).toBeDisabled()
})
it('invalidates receipt stock, lots, expiration and POS search only in the affected tenant/branch', async () => {
  const fake = operationServer(); let client!: QueryClient
  const capture = (value: QueryClient) => { client = value; for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) {
    client.setQueryData(scopedKey(workspace, resource, workspace.session.userId, workspace.access.membershipId), { unit: true })
    client.setQueryData(scopedKey({ ...workspace, branchId: id(99) }, resource), { unit: true })
  } }
  mountOperation(fake, <><PurchasesPage /><CacheProbe capture={capture} /></>, loaded(fake))
  const checkbox = await screen.findByRole('checkbox', { name: /recibí físicamente/ }); await waitFor(() => expect(checkbox).toBeEnabled()); fireEvent.click(checkbox); fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción una vez' }))
  await screen.findByRole('heading', { name: 'MERCADERÍA REGISTRADA' })
  for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) {
    expect(client.getQueryState(scopedKey(workspace, resource, workspace.session.userId, workspace.access.membershipId))?.isInvalidated).toBe(true)
    expect(client.getQueryState(scopedKey({ ...workspace, branchId: id(99) }, resource))?.isInvalidated).toBe(false)
  }
})
it('blocks another creation on lost response and reference lookup does not silently acknowledge it', async () => {
  const fake = operationServer(); fake.state.loseCreate = true; const view = mountOperation(fake, <PurchasesPage />)
  if (!screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia documental (opcional)' }), { target: { value: 'REF-UNIT' } })
  if (screen.getByRole('button', { name: 'Nueva recepción' }).getAttribute('disabled') === null && !screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar con productos y lotes' }))
  await screen.findByRole('heading', { name: 'Recepción pendiente de conciliación' })
  expect(screen.getByRole('button', { name: 'Nueva recepción' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia de la recepción a consultar' }), { target: { value: 'REF-UNIT' } })
  fireEvent.click(screen.getByRole('button', { name: 'Buscar referencia' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar recepción de ' + time }))
  await screen.findByRole('region', { name: 'Productos y lotes' })
  expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled()
  expect(screen.getByTestId('location').textContent).toContain('receiptPending=create'); expect(fake.writes()).toHaveLength(1)
  view.unmount(); mountOperation(fake, <PurchasesPage />, '?receiptPending=create&purchase=' + id(20))
  await screen.findByRole('region', { name: 'Productos y lotes' }); expect(screen.getByRole('button', { name: 'Nueva recepción' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('never reconciles a lost creation merely by finding an older confirmed receipt with that reference', async () => {
  const fake = operationServer(); fake.state.loseCreate = true; fake.state.purchases = [{ ...purchase, id: id(99), status: 'confirmed', confirmedAt: time, confirmedByActorId: id(1) }]
  mountOperation(fake, <PurchasesPage />)
  if (!screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia documental (opcional)' }), { target: { value: 'REF-UNIT' } }); fireEvent.click(screen.getByRole('button', { name: 'Continuar con productos y lotes' }))
  await screen.findByRole('heading', { name: 'Recepción pendiente de conciliación' })
  fireEvent.change(screen.getByRole('textbox', { name: 'Referencia de la recepción a consultar' }), { target: { value: 'REF-UNIT' } }); fireEvent.click(screen.getByRole('button', { name: 'Buscar referencia' }))
  const matches = await screen.findAllByRole('button', { name: 'Consultar recepción de ' + time }); fireEvent.click(matches[0]!)
  await screen.findByRole('heading', { name: 'MERCADERÍA REGISTRADA' })
  expect(screen.getByTestId('location').textContent).toContain('receiptPending=create'); expect(screen.getByRole('button', { name: 'Nueva recepción' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('keeps an uncertain line replacement blocked even when an early read shows a draft', async () => {
  const fake = operationServer(); fake.state.loseSave = true; mountOperation(fake, <PurchasesPage />)
  await begin(fake); await pickProduct()
  for (const [label, value] of [['Cantidad de presentaciones', '2'], ['Costo por presentación (S/)', '5'], ['Número de lote', 'BATCH-UNIT']]) fireEvent.change(screen.getByRole('textbox', { name: label }), { target: { value } })
  fireEvent.change(screen.getByRole('combobox', { name: 'Presentación de compra' }), { target: { value: id(12) } })
  fireEvent.change(screen.getByLabelText('Vencimiento del lote'), { target: { value: '2027-12-31' } })
  fireEvent.click(screen.getByRole('button', { name: 'Agregar línea a la recepción' })); fireEvent.click(screen.getByRole('checkbox', { name: /Revisé todas las líneas/ })); fireEvent.click(screen.getByRole('button', { name: 'Guardar líneas y revisar recepción' }))
  await screen.findByRole('heading', { name: 'Recepción pendiente de conciliación' })
  expect(screen.getByTestId('location').textContent).toContain('receiptPending=lines')
  expect(screen.getByRole('button', { name: 'Confirmar recepción una vez' })).toBeDisabled()
  expect(fake.writes().filter(([, init]) => init?.method === 'PUT')).toHaveLength(1)
})
it.each([true, false])('reconciles lost confirmation only after observing confirmed (still draft = %s)', async remainsDraft => {
  const fake = operationServer(); fake.state.loseConfirm = true; fake.state.confirmRemainsDraft = remainsDraft
  mountOperation(fake, <PurchasesPage />, loaded(fake))
  const checkbox = await screen.findByRole('checkbox', { name: /recibí físicamente/ }); await waitFor(() => expect(checkbox).toBeEnabled())
  fireEvent.click(checkbox); fireEvent.click(screen.getByRole('button', { name: 'Confirmar recepción una vez' }))
  await waitFor(() => expect(fake.writes()).toHaveLength(1))
  await waitFor(() => remainsDraft ? expect(screen.getByRole('heading', { name: 'Recepción pendiente de conciliación' })).toBeInTheDocument() : expect(screen.getByRole('heading', { name: 'MERCADERÍA REGISTRADA' })).toBeInTheDocument())
  if (remainsDraft) {
    fireEvent.click(screen.getByRole('button', { name: 'Consultar estado de la misma recepción' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar estado de la misma recepción' })).toBeEnabled())
    expect(screen.getByTestId('location').textContent).toContain('receiptPending=confirm')
    expect(screen.getByRole('button', { name: 'Confirmar recepción una vez' })).toBeDisabled()
  }
  expect(fake.writes()).toHaveLength(1)
})
it('blocks medicines without lots and new products without presentations, never calling units PUT', async () => {
  const fake = operationServer(); fake.state.detail.units = []; mountOperation(fake, <PurchasesPage />)
  await begin(fake); await pickProduct()
  expect(screen.getByText(/Owner debe configurar o activar una presentación/)).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Agregar línea a la recepción' })).toBeDisabled()
  expect(fake.writes()).toHaveLength(1)
})
it('creates suppliers with optional RUC/contact only for Owner and paginates suppliers on the server', async () => {
  const fake = operationServer(); fake.state.suppliers = Array.from({ length: 21 }, (_, index) => ({ id: id(100 + index), name: 'Proveedor ' + index, ruc: null, contact: null, isActive: true, createdAt: time }))
  mountOperation(fake, <PurchasesPage />)
  fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' })); fireEvent.click(screen.getByRole('button', { name: 'Elegir proveedor' }))
  const next = await screen.findByRole('button', { name: 'Página siguiente' }); await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next); await screen.findByRole('button', { name: 'Elegir Proveedor 20' })
  fireEvent.click(screen.getByRole('button', { name: 'Nuevo proveedor' }))
  const form = within(screen.getByRole('form', { name: 'Crear proveedor' }))
  fireEvent.change(form.getByRole('textbox', { name: 'Nombre del proveedor' }), { target: { value: 'Proveedor sin RUC unitario' } }); fireEvent.click(form.getByRole('button', { name: 'Crear proveedor una vez' }))
  await screen.findByText(/Proveedor creado mediante la API/)
  expect(fake.writes()[0]?.[1]?.body).toBe('{"name":"Proveedor sin RUC unitario","ruc":null,"contact":null}')
})
it('allows Pharmacist purchasing but no supplier administration, and rejects Cashier before any query', async () => {
  const fake = operationServer(); const pharmacist = { ...workspace, access: { ...workspace.access, role: 'pharmacist' as const } }
  const view = mountOperation(fake, <PurchasesPage />, '', pharmacist)
  fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' })); fireEvent.click(screen.getByRole('button', { name: 'Elegir proveedor' }))
  await screen.findByText('No hay proveedores en esta página.'); expect(screen.queryByRole('form', { name: 'Crear proveedor' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Continuar con productos y lotes' })).toBeEnabled()
  view.unmount(); const empty = operationServer(); mountOperation(empty, <PurchasesPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } })
  expect(screen.getByText(/Compras requiere Owner o Pharmacist/)).toBeInTheDocument(); expect(empty.fetcher).not.toHaveBeenCalled()
})
it.each([403, 409, 429])('keeps HTTP %s visible without an automatic retry or a false success', async status => {
  const fake = operationServer(); fake.state.reject = status; mountOperation(fake, <PurchasesPage />)
  if (screen.getByRole('button', { name: 'Nueva recepción' }).getAttribute('disabled') === null && !screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar con productos y lotes' }))
  await screen.findByText('No pudimos completar la operación de compra'); expect(fake.writes()).toHaveLength(1)
  expect(screen.queryByRole('heading', { name: 'MERCADERÍA REGISTRADA' })).not.toBeInTheDocument()
})
it('aborts an in-flight write on context change and discards its late response', async () => {
  const fake = operationServer(); let finish!: () => void; let signal: AbortSignal | null = null
  const normal = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, init) => {
    if (init?.method === 'POST' && String(path).endsWith('/purchases')) { signal = init.signal as AbortSignal; await new Promise<void>(resolve => { finish = resolve }); return json(purchase, 201) }
    return normal(path, init)
  })
  const view = mountOperation(fake, <PurchasesPage />)
  if (screen.getByRole('button', { name: 'Nueva recepción' }).getAttribute('disabled') === null && !screen.queryByRole('textbox', { name: 'Referencia documental (opcional)' })) fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' }))
  fireEvent.click(screen.getByRole('button', { name: 'Continuar con productos y lotes' }))
  await waitFor(() => expect(finish).toBeTypeOf('function'))
  view.changeContext({ ...workspace, branchId: id(99), branch: { ...workspace.branch, id: id(99), name: 'Otra sede unitaria' } })
  expect(signal!.aborted).toBe(true); await act(async () => { finish() })
  expect(screen.queryByRole('region', { name: 'Recepción persistida' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(1)
})
it('cancels the initial receipt read when starting a new one so its late response cannot replace the workspace', async () => {
  const fake = operationServer(); let finish!: () => void; let signal: AbortSignal | null = null
  const normal = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, init) => {
    if (init?.method === 'GET' && String(path).endsWith('/' + id(20))) { signal = init.signal as AbortSignal; await new Promise<void>(resolve => { finish = resolve }); return json({ purchase, lines: [storedLine] }) }
    return normal(path, init)
  })
  mountOperation(fake, <PurchasesPage />, '?purchase=' + id(20)); await waitFor(() => expect(finish).toBeTypeOf('function'))
  fireEvent.click(screen.getByRole('button', { name: 'Nueva recepción' })); fireEvent.click(screen.getByRole('button', { name: 'Continuar con otra recepción' })); expect(signal!.aborted).toBe(true)
  await act(async () => { finish() })
  expect(screen.queryByRole('region', { name: 'Recepción persistida' })).not.toBeInTheDocument(); expect(screen.getByRole('form', { name: 'Nueva recepción' })).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
