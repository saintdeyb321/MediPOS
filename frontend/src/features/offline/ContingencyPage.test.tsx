import { IDBFactory } from 'fake-indexeddb'
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ContingencyPage } from './ContingencyPage'
import { OutboxTable } from './OfflinePage'
import { offlineStorage } from './storage'
import { envelope, id, item, lease } from './offline.test.support'
import { scopeKey } from './storage'
import { employeeServer, mountEmployees } from '../employees/employees.test.support'

beforeEach(() => vi.stubGlobal('indexedDB', new IDBFactory()))
it('requires a real online preparation instead of inventing a session, products or a cash session', async () => {
  const fake = employeeServer(); mountEmployees(fake, <ContingencyPage />)
  await screen.findByText(/No hay una preparación vigente/)
  expect(screen.queryByRole('textbox', { name: 'Buscar en catálogo descargado' })).not.toBeInTheDocument()
  expect(fake.fetcher).not.toHaveBeenCalled()
})
it('links only a received accepted event to the existing authenticated sale/ticket route', () => {
  const l = lease(); const e = envelope(); const fake = employeeServer(); const base = `/boticas/${l.tenantId}/sucursales/${l.branchId}`
  mountEmployees(fake, <OutboxTable base={base} rows={[{ id: e.eventId, scope: scopeKey(l), envelope: e, status: 'accepted', attempts: 1, nextAttemptAt: 0,
    result: { eventId: e.eventId, status: 'accepted', resultCode: 'sync.sale_accepted', receiptId: id(30), resultReferenceId: id(31), receivedAtUtc: e.clientOccurredAtUtc, processedAtUtc: e.clientOccurredAtUtc, isReplay: false, retryable: false } }]} />)
  expect(screen.getByRole('link', { name: 'Consultar venta y ticket con conexión' })).toHaveAttribute('href', base + '/ventas?sale=' + id(31))
  expect(fake.fetcher).not.toHaveBeenCalled()
})
it('displays downloaded exact prices, queues one durable provisional event after consent, and never confirms it locally', async () => {
  const l = lease(); await offlineStorage.prepare(l, [item()], true)
  const fake = employeeServer(); const view = mountEmployees(fake, <ContingencyPage />)
  fireEvent.change(await screen.findByRole('textbox', { name: 'Buscar en catálogo descargado' }), { target: { value: 'sintético' } })
  await screen.findByRole('heading', { name: 'Producto sintético unitario' })
  expect(screen.getByText(/Precio por unidad base: S\/ 0.0001/)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Base' })); expect(screen.getByRole('button', { name: 'Base' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: 'Importe declarado, sin procesamiento externo' }), { target: { value: '0.0001' } })
  const save = screen.getByRole('button', { name: 'Guardar una vez en outbox local' }); expect(save).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /autorizo guardar este evento pendiente y sincronizarlo/ })); fireEvent.click(save); fireEvent.click(save)
  await screen.findByRole('cell', { name: 'Pendiente: sin confirmar' })
  expect((await offlineStorage.outbox(l))).toHaveLength(1); expect(fake.writes()).toHaveLength(0)
  expect(screen.queryByText(/Aceptada por el servidor|VENTA REALIZADA/)).not.toBeInTheDocument()
  view.unmount(); mountEmployees(fake, <ContingencyPage />)
  await waitFor(() => expect(screen.getByRole('cell', { name: 'Pendiente: sin confirmar' })).toBeInTheDocument())
  expect((await offlineStorage.outbox(l))).toHaveLength(1); expect(fake.writes()).toHaveLength(0)
})
