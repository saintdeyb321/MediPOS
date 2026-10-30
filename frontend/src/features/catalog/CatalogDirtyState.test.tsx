import { useState } from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router'
import { RuntimeProvider } from '../../app/providers/runtime'
import { createMediPosService, WorkspaceContext } from '../access'
import type { Workspace } from '../access'
import { HttpClient } from '../../shared/api/http'
import { JsonNumber, parseExactJson, stringifyExactJson } from '../../shared/api/exactJson'
import { Modal } from '../../shared/ui/Modal'
import { UnitEditor } from './ProductDetailPanel'
import { useCatalogWrite } from './useCatalogWrite'
import type { ProductDetail, ProductUnit } from './api'

// Synthetic UI transport only: protects edit/recovery state, not proof of a PostgreSQL write.
const id = (n: number) => `019a0162-0000-7000-8000-${String(n).padStart(12, '0')}`
const now = '2026-10-29T12:00:00Z'
const unit: ProductUnit = { id: id(8), name: 'Unidad sintética', conversionToBase: new JsonNumber('1'), isBaseUnit: true, isActive: true }
const detail: ProductDetail = { revision: '1', units: [unit], product: { id: id(6), tenantId: id(2), globalProductId: null, internalCode: 'TEST', productType: 'retail', name: 'Producto sintético', categoryId: id(7), brandOrLaboratory: 'Prueba', barcode: null, medicine: null, retailPrice: new JsonNumber('1'), wholesalePrice: null, isActive: true, createdAt: now } }
const workspace: Workspace = { tenantId: id(2), branchId: id(3), tenantName: 'Prueba de componente', base: `/boticas/${id(2)}/sucursales/${id(3)}`, session: { userId: id(1) },
  branch: { id: id(3), tenantId: id(2), legalEntityId: id(4), name: 'Prueba', isMainHub: false, createdAt: now },
  access: { userId: id(1), tenantId: id(2), branchId: id(3), membershipId: id(5), role: 'owner', membershipIsActive: true, branchAssigned: true, withinSchedule: true, licenseStatus: 'trial', licenseStartsAt: null, licenseExpiresAt: null, evaluatedAtUtc: now } }
function mount(lost = false) {
  let units = [unit]; let version = 1
  const json = (value: unknown) => new Response(stringifyExactJson(value), { headers: { 'Content-Type': 'application/json' } })
  const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path, init) => {
    if (String(path) === '/auth/csrf') return json({ requestToken: 'synthetic-csrf' })
    if (String(path).endsWith('/unit-configuration')) {
      if (init?.method === 'PUT') { units = (parseExactJson(String(init.body)) as { units: ProductUnit[] }).units; version++; if (lost) { units = [{ ...unit, name: 'Lectura concurrente sintética' }]; throw new TypeError('Synthetic lost response') } }
      return json({ units, version })
    }
    return json({ ...detail, revision: String(version), units })
  })
  function Editor() {
    const [open, setOpen] = useState(true); const [dirty, setDirty] = useState(false); const [epoch, setEpoch] = useState(0)
    const write = useCatalogWrite(() => {}, () => { setDirty(false); setEpoch(value => value + 1) })
    return <><output data-testid="dirty">{String(dirty)}</output><output data-testid="pending">{String(write.pending)}</output>
      <Modal open={open} title="Editar presentaciones" dirty={dirty} busy={write.busy} recovery={write.pending} onClose={() => setOpen(false)} onRecoveryClose={() => setOpen(false)}>
        <UnitEditor key={epoch} detail={detail} write={write} onDirty={setDirty} />
      </Modal></>
  }
  const http = new HttpClient(fetcher)
  render(<RuntimeProvider runtime={{ http, service: createMediPosService(http), preview: false }}><MemoryRouter><WorkspaceContext.Provider value={workspace}><Editor /></WorkspaceContext.Provider></MemoryRouter></RuntimeProvider>)
  return fetcher
}
it('opening and reverting presentation edits are clean; confirmed save closes without false discard', async () => {
  const fetcher = mount()
  const input = await screen.findByLabelText('Nombre de presentación 1')
  expect(screen.getByTestId('dirty')).toHaveTextContent('false')
  fireEvent.change(input, { target: { value: 'Edición sintética' } }); expect(screen.getByTestId('dirty')).toHaveTextContent('true')
  fireEvent.change(input, { target: { value: unit.name } }); expect(screen.getByTestId('dirty')).toHaveTextContent('false')
  fireEvent.change(input, { target: { value: 'Edición confirmada' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé nombres/ })); fireEvent.click(screen.getByRole('button', { name: 'Guardar presentaciones' }))
  await waitFor(() => expect(screen.getByTestId('pending')).toHaveTextContent('false'))
  await waitFor(() => expect(screen.getByTestId('dirty')).toHaveTextContent('false'))
  expect(await screen.findByDisplayValue('Edición confirmada')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar Editar presentaciones' }))
  expect(screen.queryByText('¿Salir del formulario?')).not.toBeInTheDocument()
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(1)
})
it('a recovery read preserves original edits and blocks resubmission while allowing a safe exit', async () => {
  const fetcher = mount(true)
  fireEvent.change(await screen.findByLabelText('Nombre de presentación 1'), { target: { value: 'Intención original' } })
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé nombres/ })); fireEvent.click(screen.getByRole('button', { name: 'Guardar presentaciones' }))
  await waitFor(() => expect(fetcher.mock.calls.filter(([path]) => String(path).endsWith('/unit-configuration')).length).toBeGreaterThanOrEqual(3))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Volver a la página' })).toBeEnabled())
  expect(screen.getByDisplayValue('Intención original')).toBeInTheDocument(); expect(screen.getByTestId('pending')).toHaveTextContent('true')
  expect(screen.getByRole('button', { name: 'Guardar presentaciones' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Volver a la página' }))
  expect(screen.getByTestId('pending')).toHaveTextContent('true')
  expect(fetcher.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(1)
})
