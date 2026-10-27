import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { id, json, lot, mountOperation, n, operationServer, time, workspace } from '../../test/operations'
import { InventoryPage } from './InventoryPage'

async function openAdjustment() {
  fireEvent.click(screen.getByRole('button', { name: 'Lotes' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Ajustar lote BATCH-UNIT' }))
  return within(screen.getByRole('form', { name: 'Ajustar lote' }))
}
async function pickProduct() {
  fireEvent.click(screen.getByRole('button', { name: 'Filtrar por producto' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Buscar producto por nombre, ingrediente o código' }), { target: { value: 'ingrediente' } })
  fireEvent.click(screen.getByRole('button', { name: 'Buscar productos' })); fireEvent.click(await screen.findByRole('button', { name: 'Consultar Producto sintético unitario' })); fireEvent.click(await screen.findByRole('button', { name: 'Usar producto consultado' }))
  fireEvent.click(await screen.findByRole('button', { name: 'Configurar mínimo' }))
  return screen.findByRole('form', { name: 'Configurar stock mínimo' })
}
it('reads stock, lots and their dates without translating an error into empty inventory', async () => {
  const fake = operationServer(); mountOperation(fake, <InventoryPage />)
  expect(await screen.findByRole('cell', { name: '20' })).toBeInTheDocument()
  await openAdjustment(); expect(document.querySelector('time[datetime="2027-12-31"]')).toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('distinguishes returned zero, absent balance and failed read', async () => {
  const fake = operationServer(); fake.state.lots = [{ ...lot, quantityAvailableBase: n('0.000000000000') }]
  const view = mountOperation(fake, <InventoryPage />); expect(await screen.findByRole('cell', { name: '0' })).toBeInTheDocument()
  view.unmount(); fake.state.lots = []; const empty = mountOperation(fake, <InventoryPage />)
  await screen.findByText(/No hay saldos registrados/); empty.unmount()
  fake.state.failStock = true; mountOperation(fake, <InventoryPage />)
  await screen.findByText('No pudimos consultar inventario'); expect(screen.queryByText(/No hay saldos registrados/)).not.toBeInTheDocument()
})
it('uses actual page offsets for stock and lots without inventing a total', async () => {
  const fake = operationServer(); fake.state.lots = Array.from({ length: 21 }, (_, index) => ({ ...lot, inventoryLotId: id(100 + index), businessProductId: id(10 + index), batchNumber: 'Batch ' + index }))
  mountOperation(fake, <InventoryPage />); const next = await screen.findByRole('button', { name: 'Página siguiente' }); await waitFor(() => expect(next).toBeEnabled()); fireEvent.click(next)
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/inventory/stock?offset=20&limit=20'))).toBe(true))
  fireEvent.click(screen.getByRole('button', { name: 'Lotes' })); await screen.findByRole('button', { name: 'Ajustar lote Batch 0' })
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' })); await screen.findByRole('button', { name: 'Ajustar lote Batch 20' })
  expect(screen.queryByRole('button', { name: 'Ajustar lote Batch 0' })).not.toBeInTheDocument()
})
it.each(['1.000000000001', '-1.000000000001'])('records signed exact adjustment %s only after reason/review then re-reads instead of adding local stock', async quantity => {
  const fake = operationServer(); mountOperation(fake, <InventoryPage />); const form = await openAdjustment()
  const button = form.getByRole('button', { name: 'Registrar ajuste una vez' }); expect(button).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: /Variación en unidades base/ }), { target: { value: quantity } })
  fireEvent.click(form.getByRole('checkbox')); expect(button).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: 'Motivo del ajuste' }), { target: { value: 'Motivo de prueba unitaria' } }); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(button); fireEvent.click(button)
  await screen.findByRole('heading', { name: 'Ajuste registrado por el servidor' })
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toContain('"quantityDeltaBase":' + quantity)
  await waitFor(() => expect(screen.getByRole('cell', { name: quantity.startsWith('-') ? '18.999999999999' : '21.000000000001' })).toBeInTheDocument())
  expect(screen.queryByRole('form', { name: 'Ajustar lote' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/inventory/lots?')).length).toBeGreaterThanOrEqual(2)
})
it('rejects zero, insufficient negative quantity and missing reason before dispatch', async () => {
  const fake = operationServer(); mountOperation(fake, <InventoryPage />); const form = await openAdjustment()
  fireEvent.change(form.getByRole('textbox', { name: 'Motivo del ajuste' }), { target: { value: 'Motivo unitario' } })
  for (const value of ['0', '-21', '0.00000000000000000000000000001']) {
    fireEvent.change(form.getByRole('textbox', { name: /Variación en unidades base/ }), { target: { value } })
    fireEvent.click(form.getByRole('checkbox')); expect(form.getByRole('button', { name: 'Registrar ajuste una vez' })).toBeDisabled()
  }
  expect(fake.writes()).toHaveLength(0)
})
it('keeps an uncertain adjustment blocked after refreshing unchanged or changed balance and after reload', async () => {
  const fake = operationServer(); fake.state.loseAdjustment = true; const view = mountOperation(fake, <InventoryPage />); const form = await openAdjustment()
  fireEvent.change(form.getByRole('textbox', { name: /Variación en unidades base/ }), { target: { value: '1' } }); fireEvent.change(form.getByRole('textbox', { name: 'Motivo del ajuste' }), { target: { value: 'Motivo' } }); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Registrar ajuste una vez' }))
  await waitFor(() => expect(screen.getByTestId('location').textContent).toContain('inventoryPending=adjustment'))
  fireEvent.click(screen.getByRole('button', { name: 'Actualizar consulta' })); await waitFor(() => expect(screen.getByRole('cell', { name: '21' })).toBeInTheDocument())
  expect(screen.getByRole('button', { name: 'Ajustar lote BATCH-UNIT' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
  view.unmount(); mountOperation(fake, <InventoryPage />, '?inventoryPending=adjustment'); fireEvent.click(screen.getByRole('button', { name: 'Lotes' }))
  expect(await screen.findByRole('button', { name: 'Ajustar lote BATCH-UNIT' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('sets exact minimum only as Owner, labels its absent GET truthfully, and filters with the selected product ID', async () => {
  const fake = operationServer(); mountOperation(fake, <InventoryPage />); const form = within(await pickProduct())
  expect(form.getByText(/La API no ofrece lectura del mínimo actual/)).toBeInTheDocument()
  fireEvent.change(form.getByRole('textbox', { name: 'Mínimo de stock base' }), { target: { value: '1,000000000001' } }); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Guardar mínimo' }))
  await screen.findByRole('heading', { name: 'Mínimo guardado por el servidor' })
  expect(fake.writes()[0]?.[1]?.body).toBe('{"minimumStockBase":1.000000000001}')
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/inventory/stock?') && String(path).includes('productId=' + id(10)))).toBe(true)
  expect(fake.fetcher.mock.calls.some(([path, init]) => String(path).endsWith('/stock-threshold') && init?.method === 'GET')).toBe(false)
})
it('preserves unknown minimum outcome without retry and restores its blocking marker', async () => {
  const fake = operationServer(); fake.state.loseMinimum = true; mountOperation(fake, <InventoryPage />); const form = within(await pickProduct())
  fireEvent.change(form.getByRole('textbox', { name: 'Mínimo de stock base' }), { target: { value: '0' } }); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Guardar mínimo' }))
  await waitFor(() => expect(screen.getByTestId('location').textContent).toContain('inventoryPending=minimum'))
  expect(form.getByRole('button', { name: 'Guardar mínimo' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('shows cost only as Owner, preserves null cost and does not grant Pharmacist minimum editing', async () => {
  const fake = operationServer(); fake.state.expiring = [{ inventoryLotId: id(30), branchId: id(5), businessProductId: id(10), productName: 'Producto sintético unitario', quantityAvailableBase: n('2'), expirationDate: '2026-10-20', costValue: null, potentialSaleValue: n('2.1234') }]
  const view = mountOperation(fake, <InventoryPage />); fireEvent.click(screen.getByRole('button', { name: 'Próximos a vencer (30 días)' }))
  expect(await screen.findByRole('columnheader', { name: 'Valor de costo' })).toBeInTheDocument(); expect(screen.getByRole('cell', { name: 'No disponible' })).toBeInTheDocument()
  view.unmount(); fake.state.expiring[0] = { ...fake.state.expiring[0]!, costValue: n('123.4567') }
  mountOperation(fake, <InventoryPage />, '', { ...workspace, access: { ...workspace.access, role: 'pharmacist' } }); fireEvent.click(screen.getByRole('button', { name: 'Próximos a vencer (30 días)' }))
  await screen.findByRole('cell', { name: '2026-10-20' }); expect(screen.queryByRole('columnheader', { name: 'Valor de costo' })).not.toBeInTheDocument(); expect(screen.queryByText('S/ 123.4567')).not.toBeInTheDocument()
  expect(screen.queryByRole('form', { name: 'Configurar stock mínimo' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('denies Cashier before querying inventory', () => {
  const fake = operationServer(); mountOperation(fake, <InventoryPage />, '', { ...workspace, access: { ...workspace.access, role: 'cashier' } })
  expect(screen.getByText(/Inventario requiere Owner o Pharmacist/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it.each([403, 409, 429])('shows HTTP %s from an adjustment without retry or success', async status => {
  const fake = operationServer(); fake.state.reject = status; mountOperation(fake, <InventoryPage />); const form = await openAdjustment()
  fireEvent.change(form.getByRole('textbox', { name: /Variación en unidades base/ }), { target: { value: '1' } }); fireEvent.change(form.getByRole('textbox', { name: 'Motivo del ajuste' }), { target: { value: 'Motivo' } }); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Registrar ajuste una vez' }))
  await screen.findByText('No pudimos completar la operación de inventario'); expect(fake.writes()).toHaveLength(1)
  expect(screen.queryByRole('heading', { name: 'Ajuste registrado por el servidor' })).not.toBeInTheDocument()
})
it('aborts reads on context change without leaking the previous branch stock', async () => {
  const fake = operationServer(); let finish!: () => void; let signal: AbortSignal | null = null
  const normal = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, init) => {
    if (String(path).includes('/inventory/stock?') && String(path).includes('/branches/' + id(5))) { signal = init?.signal as AbortSignal; await new Promise<void>(resolve => { finish = resolve }); return json([{ businessProductId: id(10), quantityAvailableBase: n('999.1234') }]) }
    return normal(path, init)
  })
  const view = mountOperation(fake, <InventoryPage />); await waitFor(() => expect(finish).toBeTypeOf('function'))
  view.changeContext({ ...workspace, branchId: id(99), branch: { ...workspace.branch, id: id(99), name: 'Otra sede unitaria', createdAt: time } })
  expect(signal!.aborted).toBe(true); await act(async () => { finish() })
  expect(screen.queryByRole('cell', { name: '999.1234' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
