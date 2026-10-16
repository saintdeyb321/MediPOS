import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { SettingsPage } from './SettingsPage'
import { settingsApi } from './api'
import { HttpClient } from '../../shared/api/http'
import { id, json, managementServer, mountTransfers, workspace } from '../notifications/management.test.support'

it.each(['pharmacist', 'cashier'] as const)('denies configuration to %s before any directory request', role => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />, '', { ...workspace, access: { ...workspace.access, role } }); expect(screen.getByText('Configuración requiere Owner.')).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('loads actual license and entity/branch relationships and disables creation at the server-derived branch limit', async () => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />)
  await screen.findByText('trial'); await screen.findByText(/Sede unitaria · Entidad: Entidad sintética unitaria/)
  expect(screen.getByRole('button', { name: 'Crear sucursal' })).toBeDisabled(); expect(screen.getByText(/Límite de sucursales de la licencia alcanzado/)).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
  expect(screen.queryByRole('button', { name: /Renovar|Activar|Editar|Eliminar/ })).not.toBeInTheDocument()
})
async function prepareLegal() { fireEvent.change(screen.getByRole('textbox', { name: 'Razón social' }), { target: { value: 'Nueva entidad unitaria' } }); fireEvent.change(screen.getByRole('textbox', { name: 'RUC de la entidad' }), { target: { value: 'UNIT-LEGAL-ONLY' } }); fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo la creación persistente/ })) }
it('requires explicit confirmation, sends only backend fields and rereads directories without duplicating', async () => {
  const fake = managementServer(); mountTransfers(fake, <SettingsPage />); await screen.findByText('trial'); const button = screen.getByRole('button', { name: 'Crear entidad legal' }); expect(button).toBeDisabled()
  await prepareLegal(); fireEvent.click(button); fireEvent.click(button)
  await screen.findByText('Registro reconsultado: Nueva entidad unitaria · UNIT-LEGAL-ONLY · UTC 2026-10-16T12:00:00Z'); expect(fake.writes()).toHaveLength(1)
  expect(fake.writes()[0]![1]!.body).toBe('{"legalName":"Nueva entidad unitaria","ruc":"UNIT-LEGAL-ONLY"}')
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué inequívocamente/ })); fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación' })); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('settingsPending'))
})
it('creates a branch only with an observed legal entity and permits no license changes', async () => {
  const fake = managementServer(); fake.management.license.maxBranches = 5; mountTransfers(fake, <SettingsPage />); await screen.findByText('trial')
  fireEvent.change(screen.getByRole('textbox', { name: 'Nombre de la nueva sucursal' }), { target: { value: 'Sucursal unitaria nueva' } }); fireEvent.change(screen.getByRole('combobox', { name: 'Entidad legal existente' }), { target: { value: id(4) } }); fireEvent.click(screen.getByRole('checkbox', { name: /Confirmo crear Sucursal/ })); fireEvent.click(screen.getByRole('button', { name: 'Crear sucursal' }))
  await screen.findByText(/Registro reconsultado: Sucursal unitaria nueva/); expect(fake.writes()).toHaveLength(1); expect(String(fake.writes()[0]![0])).toMatch(/\/branches$/); expect(fake.management.branches).toHaveLength(3)
})
it('keeps unknown legal creation blocked, permits reread/reconciliation and never assumes name is idempotency', async () => {
  const fake = managementServer(); fake.management.lose = 'create'; mountTransfers(fake, <SettingsPage />); await screen.findByText('trial'); await prepareLegal(); fireEvent.click(screen.getByRole('button', { name: 'Crear entidad legal' }))
  await screen.findByRole('heading', { name: 'Alta pendiente de conciliación' }); await within(screen.getByRole('combobox', { name: 'Registro original a revisar' })).findByRole('option', { name: 'Nueva entidad unitaria · UNIT-LEGAL-ONLY' }); expect(screen.getByRole('button', { name: 'Crear entidad legal' })).toBeDisabled()
  fireEvent.change(screen.getByRole('combobox', { name: 'Registro original a revisar' }), { target: { value: id(540) } }); expect(screen.getByRole('button', { name: 'Continuar tras conciliación' })).toBeDisabled(); fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué inequívocamente/ })); fireEvent.click(screen.getByRole('button', { name: 'Continuar tras conciliación' })); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('settingsPending')); expect(fake.writes()).toHaveLength(1)
})
it.each([400, 403, 409, 429, 503])('preserves HTTP %i without automatic configuration retry', async status => {
  const fake = managementServer(); fake.management.reject = status; mountTransfers(fake, <SettingsPage />); await screen.findByText('trial'); await prepareLegal(); fireEvent.click(screen.getByRole('button', { name: 'Crear entidad legal' })); await screen.findAllByRole('alert'); await waitFor(() => expect(fake.writes()).toHaveLength(1)); expect(fake.management.legal).toHaveLength(1)
  if (status === 503) expect(screen.getByTestId('location')).toHaveTextContent('settingsPending=legal')
})
it('restores an acknowledged creation for review after reload and pages actual legal records', async () => {
  const fake = managementServer(); fake.management.legal = Array.from({ length: 21 }, (_, i) => ({ ...fake.management.legal[0]!, id: id(600 + i), legalName: 'Entidad ' + i })); mountTransfers(fake, <SettingsPage />, '?settingsPending=legal&settingsCreated=' + id(620)); await screen.findByText('Entidad 0 · RUC UNIT-NO-FISCAL')
  const area = screen.getByRole('heading', { name: 'Entidades legales' }).closest('section')!; fireEvent.click(within(area).getByRole('button', { name: 'Página siguiente' })); await screen.findByText(/Registro reconsultado: Entidad 20/); expect(fake.writes()).toHaveLength(0)
})
it('rejects directories for a foreign tenant and malformed license before showing data', async () => {
  const fake = managementServer(); fake.management.legal[0] = { ...fake.management.legal[0]!, tenantId: id(999) }; const api = settingsApi(new HttpClient(fake.fetcher)); await expect(api.legal(workspace, 0)).rejects.toMatchObject({ code: 'client.unexpected_response' }); fake.management.license.maxBranches = 6; await expect(api.license(workspace)).rejects.toMatchObject({ code: 'client.unexpected_response' })
})
it('cancels old private directory requests when switching memberships', async () => {
  const fake = managementServer(); let finish!: (response: Response) => void; fake.management.delay = new Promise(resolve => { finish = resolve }); const view = mountTransfers(fake, <SettingsPage />); await waitFor(() => expect(fake.fetcher.mock.calls.some(([p]) => String(p).includes('/legal-entities?'))).toBe(true)); const signal = fake.fetcher.mock.calls.find(([p]) => String(p).includes('/legal-entities?'))![1]!.signal!
  fake.management.delay = null; fake.management.legal = []; view.changeContext({ ...workspace, access: { ...workspace.access, membershipId: id(980) } }); expect(signal.aborted).toBe(true); await act(async () => finish(json([{ id: id(981), tenantId: workspace.tenantId, legalName: 'LATE-PRIVATE-LEGAL', ruc: 'UNIT', createdAt: '2026-10-16T12:00:00Z' }]))); expect(screen.queryByText(/LATE-PRIVATE-LEGAL/)).not.toBeInTheDocument()
})
