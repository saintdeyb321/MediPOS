import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { CommissionsPage } from './CommissionsPage'
import { commissionServer, detail, id, mountTransfers, rule, workspace } from './commissions.test.support'

async function enable() { const toggle = await screen.findByRole('button', { name: 'Cambiar habilitación' }); await waitFor(() => expect(toggle).toBeEnabled()); fireEvent.click(toggle); const check = await screen.findByRole('checkbox', { name: /autorizo habilitar/ }); await waitFor(() => expect(check).toBeEnabled()); const button = screen.getByRole('button', { name: 'Confirmar habilitación de comisiones' }); expect(button).toBeDisabled(); fireEvent.click(check); fireEvent.click(button); fireEvent.click(button) }
async function saveRule() {
  fireEvent.click(screen.getByRole('button', { name: 'Nueva regla' }))
  const area = within(screen.getByRole('region', { name: 'Nueva regla de comisión' })); fireEvent.change(area.getByRole('textbox', { name: /Buscar producto/ }), { target: { value: 'sintético' } }); fireEvent.click(area.getByRole('button', { name: 'Buscar productos' }))
  fireEvent.click(await area.findByRole('button', { name: 'Consultar ' + detail.product.name })); fireEvent.click(await area.findByRole('button', { name: 'Usar producto consultado' }))
  fireEvent.change(area.getByRole('textbox', { name: 'Comisión fija PEN por unidad base' }), { target: { value: '1.0001' } }); fireEvent.click(area.getAllByText('Entrada UTC avanzada y precisión exacta')[0]!); fireEvent.change(area.getByRole('textbox', { name: 'Vigente desde: instante UTC exacto' }), { target: { value: rule.validFrom } }); fireEvent.click(area.getByRole('checkbox', { name: /Revisé producto, valor/ }))
  const button = area.getByRole('button', { name: 'Confirmar nueva regla de comisión' }); fireEvent.click(button); fireEvent.click(button)
}
it('does not create settings on read, then enables once after explicit review and reconsults', async () => {
  const fake = commissionServer(); mountTransfers(fake, <CommissionsPage />); await screen.findByText(/Sin configurar · Comisiones deshabilitadas/); expect(fake.writes()).toHaveLength(0)
  await enable()
  await screen.findAllByText(/Operación reconocida y estado actual reconsultado/); expect(fake.writes()).toHaveLength(1); expect(fake.commissionState.configuration.isEnabled).toBe(true)
})
it.each(['cashier', 'pharmacist'] as const)('blocks %s before queries or commission configuration', role => {
  const fake = commissionServer(); mountTransfers(fake, <CommissionsPage />, '', { ...workspace, access: { ...workspace.access, role } }); expect(screen.getByText(/requiere Owner/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('creates a new rule only with an actual active product, exact values and explicit UTC validity', async () => {
  const fake = commissionServer(); mountTransfers(fake, <CommissionsPage />); await screen.findByText(/Sin configurar · Comisiones deshabilitadas/)
  await saveRule()
  await screen.findAllByText(/Operación reconocida y estado actual reconsultado/); expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toContain('"value":1.0001')
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/commissions/rules?') && String(path).includes('productId=' + detail.product.id))).toBe(true)
})
it('reconciles a known original rule by ID even when a concurrent replacement already deactivated it', async () => {
  const fake = commissionServer(); fake.commissionState.concurrent = true; mountTransfers(fake, <CommissionsPage />); await screen.findByText(/Sin configurar · Comisiones deshabilitadas/); await saveRule()
  await screen.findAllByText(/Una regla original puede estar inactiva por un cambio posterior/); expect(fake.commissionState.rules.find(row => row.id === id(601))?.isActive).toBe(false); expect(fake.writes()).toHaveLength(1)
  await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('commissionPending'))
})
it('does not treat matching settings after a lost response as proof of completion, including reload', async () => {
  const fake = commissionServer(); fake.commissionState.lose = 'settings'; const view = mountTransfers(fake, <CommissionsPage />); await enable(); await screen.findAllByRole('heading', { name: 'Cambio pendiente de conciliación' })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar operación de comisión original' })).toBeEnabled()); expect(fake.commissionState.configuration.isEnabled).toBe(true)
  expect(screen.getByRole('button', { name: 'Cambiar habilitación' })).toBeDisabled(); const search = screen.getByTestId('location').textContent!; view.unmount(); mountTransfers(fake, <CommissionsPage />, search)
  await screen.findAllByRole('heading', { name: 'Cambio pendiente de conciliación' }); expect(screen.getByRole('button', { name: 'Nueva regla' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('keeps an unidentified rule replacement blocked instead of selecting a newer coincident rule', async () => {
  const fake = commissionServer(); fake.commissionState.lose = 'rule'; fake.commissionState.concurrent = true; mountTransfers(fake, <CommissionsPage />); await screen.findByText(/Sin configurar · Comisiones deshabilitadas/); await saveRule()
  await screen.findAllByRole('heading', { name: 'Cambio pendiente de conciliación' }); expect(screen.getByTestId('location')).toHaveTextContent('commissionPending=rule'); expect(screen.getByRole('button', { name: 'Nueva regla' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('deactivates the same rule once and verifies inactive history after a lost response', async () => {
  const fake = commissionServer(); fake.commissionState.rules = [rule]; fake.commissionState.lose = 'deactivate'; mountTransfers(fake, <CommissionsPage />)
  fireEvent.click(await screen.findByRole('button', { name: 'Desactivar revisión' }))
  const check = await screen.findByRole('checkbox', { name: /Confirmo desactivar esta revisión/ }); fireEvent.click(check); fireEvent.click(screen.getByRole('button', { name: 'Desactivar esta regla' }))
  await screen.findAllByText(/Operación reconocida y estado actual reconsultado/); expect(fake.commissionState.rules[0]?.isActive).toBe(false); expect(fake.writes()).toHaveLength(1)
})
it.each([403, 409, 429, 503])('does not retry configuration on HTTP %i', async status => {
  const fake = commissionServer(); fake.commissionState.reject = status; mountTransfers(fake, <CommissionsPage />); await enable(); await screen.findByText('No pudimos completar el cambio de comisión'); expect(fake.writes()).toHaveLength(1)
})
