import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { SettingsPage } from './SettingsPage'
import { settingsApi } from './api'
import { installTestStructureStorage } from './recovery.test.support'
import { HttpClient } from '../../shared/api/http'
import { id, json, managementServer, mountTransfers, workspace } from '../notifications/management.test.support'

installTestStructureStorage()

it.each(['pharmacist', 'cashier'] as const)('denies configuration to %s before any directory request', role => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />, '', { ...workspace, access: { ...workspace.access, role } }); expect(screen.getByText('Configuración requiere Owner.')).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('loads actual license and entity/branch relationships and disables creation at the server-derived branch limit', async () => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />)
  await screen.findByText('Prueba gratuita'); const row = await screen.findByText('Sede unitaria'); expect(within(row.closest('li')!).getByText('Entidad sintética unitaria')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Nueva sucursal' })).not.toBeInTheDocument(); expect(screen.getByText(/Límite de sucursales de la licencia alcanzado/)).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
  expect(screen.queryByRole('button', { name: /Renovar|Activar|Editar|Eliminar/ })).not.toBeInTheDocument()
})
async function openLegal() { const open = await screen.findByRole('button', { name: 'Registrar otros datos fiscales' }); await waitFor(() => expect(open).toBeEnabled()); fireEvent.click(open) }
async function prepareLegal() { fireEvent.change(screen.getByRole('textbox', { name: 'Razón social' }), { target: { value: 'Nueva entidad unitaria' } }); fireEvent.change(screen.getByRole('textbox', { name: 'RUC' }), { target: { value: 'UNIT-LEGAL-ONLY' } }) }
it('sends only backend fields once and uses a valid response to close without redundant reads', async () => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />); await screen.findByText('Prueba gratuita'); await openLegal(); const button = screen.getByRole('button', { name: 'Guardar datos fiscales' }); expect(button).toBeDisabled()
  const before = fake.fetcher.mock.calls.length
  await prepareLegal(); fireEvent.click(button); fireEvent.click(button)
  await screen.findByText('Datos guardados.'); expect(fake.writes()).toHaveLength(1)
  expect(await screen.findByText('Nueva entidad unitaria')).toBeInTheDocument()
  expect(fake.writes()[0]![1]!.body).toBe('{"legalName":"Nueva entidad unitaria","ruc":"UNIT-LEGAL-ONLY"}')
  expect(screen.queryByRole('dialog', { name: 'Registrar datos del negocio' })).not.toBeInTheDocument()
  expect(screen.queryByRole('checkbox', { name: /Verifiqué que este es el registro/ })).not.toBeInTheDocument()
  const flow = fake.fetcher.mock.calls.slice(before).filter(([path]) => String(path).startsWith('/api/'))
  expect(flow.map(([path, init]) => [String(path).split('/').at(-1), init?.method])).toEqual([['access', 'GET'], ['legal-entities', 'POST']])
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('settingsPending'))
})
it('creates a branch only with an observed legal entity and permits no license changes', async () => {
  const fake = managementServer(); fake.management.license.maxBranches = 5; mountTransfers(fake, <SettingsPage />); await screen.findByText('Prueba gratuita')
  const open = screen.getByRole('button', { name: 'Nueva sucursal' }); await waitFor(() => expect(open).toBeEnabled()); fireEvent.click(open)
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre de la sucursal' }), { target: { value: 'Sucursal unitaria nueva' } }); expect(within(screen.getByRole('dialog', { name: 'Crear sucursal' })).getByText(/RUC UNIT-NO-FISCAL/)).toBeInTheDocument(); fireEvent.click(screen.getByRole('button', { name: 'Crear sucursal' }))
  await screen.findByText('Sucursal creada.'); expect(await screen.findByText('Sucursal unitaria nueva')).toBeInTheDocument(); expect(fake.writes()).toHaveLength(1); expect(String(fake.writes()[0]![0])).toMatch(/\/branches$/); expect(fake.management.branches).toHaveLength(3)
})
it('uses the same form to create fiscal data and a branch from management', async () => {
  const fake = managementServer()
  fake.management.legal = []
  fake.management.branches = []
  mountTransfers(fake, <SettingsPage />)
  await screen.findByText('Prueba gratuita')
  const open = screen.getByRole('button', { name: 'Nueva sucursal' })
  await waitFor(() => expect(open).toBeEnabled())
  fireEvent.click(open)
  await prepareLegal()
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre de la sucursal' }), { target: { value: 'Primera sede unitaria' } })
  fireEvent.click(within(screen.getByRole('dialog', { name: 'Crear sucursal' })).getByRole('button', { name: 'Crear sucursal' }))

  await screen.findByText('Sucursal creada.')
  expect(screen.queryByRole('dialog', { name: 'Crear sucursal' })).not.toBeInTheDocument()
  const writes = fake.writes().map(([path, init]) => [String(path).split('/').at(-1), JSON.parse(String(init?.body))])
  expect(writes).toEqual([
    ['legal-entities', { legalName: 'Nueva entidad unitaria', ruc: 'UNIT-LEGAL-ONLY' }],
    ['branches', { legalEntityId: fake.management.legal[0]!.id, name: 'Primera sede unitaria' }],
  ])
  expect(await screen.findByText('Primera sede unitaria')).toBeInTheDocument()
})
it('keeps unknown legal creation blocked, permits reread/reconciliation and never assumes name is idempotency', async () => {
  const fake = managementServer(); fake.management.lose = 'create'; mountTransfers(fake, <SettingsPage />); await screen.findByText('Prueba gratuita'); await openLegal(); await prepareLegal(); fireEvent.click(screen.getByRole('button', { name: 'Guardar datos fiscales' }))
  const dialog = within(screen.getByRole('dialog', { name: 'Registrar datos del negocio' }))
  await dialog.findByRole('heading', { name: 'No pudimos confirmar si se guardó' }); const lookup = dialog.getByRole('button', { name: 'Consultar resultado' }); await waitFor(() => expect(lookup).toBeEnabled()); fireEvent.click(lookup); await within(await screen.findByRole('combobox', { name: 'Registro que quieres verificar' })).findByRole('option', { name: 'Nueva entidad unitaria · UNIT-LEGAL-ONLY' }); expect(screen.getByRole('button', { name: 'Guardar datos fiscales' })).toBeDisabled()
  fireEvent.change(screen.getByRole('combobox', { name: 'Registro que quieres verificar' }), { target: { value: id(540) } }); expect(within(await screen.findByRole('dialog', { name: 'Registrar datos del negocio' })).getByRole('button', { name: 'Confirmar resultado revisado' })).toBeDisabled(); fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué que este es el registro/ })); fireEvent.click(within(await screen.findByRole('dialog', { name: 'Registrar datos del negocio' })).getByRole('button', { name: 'Confirmar resultado revisado' })); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('settingsPending')); expect(fake.writes()).toHaveLength(1)
})
it.each([400, 403, 409, 429, 503])('preserves HTTP %i without automatic configuration retry', async status => {
  const fake = managementServer(); fake.management.reject = status; mountTransfers(fake, <SettingsPage />); await screen.findByText('Prueba gratuita'); await openLegal(); await prepareLegal(); fireEvent.click(screen.getByRole('button', { name: 'Guardar datos fiscales' })); await screen.findAllByRole('alert'); await waitFor(() => expect(fake.writes()).toHaveLength(1)); expect(fake.management.legal).toHaveLength(1)
  if (status === 503) await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('settingsPending=legal'))
})
it('restores an acknowledged creation for review after reload and pages actual legal records', async () => {
  const fake = managementServer(); fake.management.legal = Array.from({ length: 21 }, (_, i) => ({ ...fake.management.legal[0]!, id: id(600 + i), legalName: 'Entidad ' + i })); mountTransfers(fake, <SettingsPage />, '?settingsPending=legal&settingsCreated=' + id(620)); await screen.findByText('Entidad 0')
  const area = screen.getByRole('heading', { name: 'Mi negocio' }).closest('section')!; fireEvent.click(within(area).getByRole('button', { name: 'Página siguiente' })); await screen.findByText('Entidad 20')
  fireEvent.click(screen.getByRole('button', { name: 'Consultar resultado' }))
  await screen.findByText(/Registro encontrado. Entidad 20/); expect(fake.writes()).toHaveLength(0)
})
it('rejects directories for a foreign tenant and malformed license before showing data', async () => {
  const fake = managementServer(); fake.management.legal[0] = { ...fake.management.legal[0]!, tenantId: id(999) }; const api = settingsApi(new HttpClient(fake.fetcher)); await expect(api.legal(workspace, 0)).rejects.toMatchObject({ code: 'client.unexpected_response' }); fake.management.license.maxBranches = 6; await expect(api.license(workspace)).rejects.toMatchObject({ code: 'client.unexpected_response' })
})
it('cancels old private directory requests when switching memberships', async () => {
  const fake = managementServer(); let finish!: (response: Response) => void; fake.management.delay = new Promise(resolve => { finish = resolve }); const view = mountTransfers(fake, <SettingsPage />); await waitFor(() => expect(fake.fetcher.mock.calls.some(([p]) => String(p).includes('/legal-entities?'))).toBe(true)); const signal = fake.fetcher.mock.calls.find(([p]) => String(p).includes('/legal-entities?'))![1]!.signal!
  fake.management.delay = null; fake.management.legal = []; view.changeContext({ ...workspace, access: { ...workspace.access, membershipId: id(980) } }); expect(signal.aborted).toBe(true); await act(async () => finish(json([{ id: id(981), tenantId: workspace.tenantId, legalName: 'LATE-PRIVATE-LEGAL', ruc: 'UNIT', createdAt: '2026-10-16T12:00:00Z' }]))); expect(screen.queryByText(/LATE-PRIVATE-LEGAL/)).not.toBeInTheDocument()
})
