import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { NotificationsPage } from './NotificationsPage'
import { NotificationCounter } from './NotificationCounter'
import { alert, id, json, managementServer, mountTransfers, n, workspace } from './management.test.support'

it('shows exact server count, historical/actionable states and rereads list/counter after explicit read', async () => {
  const fake = managementServer(); fake.management.notifications = [alert, { ...alert, notificationId: id(501), title: 'Alerta histórica unitaria', isRead: true, readAt: alert.createdAt, isCurrentlyActionable: false }]; fake.management.totalCount = n('2'); fake.management.unread = n('9007199254740993')
  mountTransfers(fake, <><NotificationCounter /><NotificationsPage /></>)
  await screen.findByText('Total del filtro: 2'); expect(screen.getByText('9007199254740993', { selector: 'small' })).toBeInTheDocument(); expect(screen.getByText(/Alerta histórica · ya no accionable/)).toBeInTheDocument()
  fireEvent.click(screen.getAllByRole('button', { name: 'Marcar como leída' })[0]!)
  await screen.findByText(/Lectura registrada para tu membresía/); await waitFor(() => expect(screen.getAllByRole('button', { name: 'Marcar como leída' }).every(b => b.hasAttribute('disabled'))).toBe(true))
  expect(fake.writes()).toHaveLength(1); expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/unread-count')).length).toBeGreaterThan(1)
})
it.each(['pharmacist', 'cashier'] as const)('sends authorized branch for %s, never asks for unfiltered notifications or branches', async role => {
  const fake = managementServer(); mountTransfers(fake, <NotificationsPage />, '', { ...workspace, access: { ...workspace.access, role } })
  await screen.findByText('Total del filtro: 1'); expect(screen.getByRole('combobox', { name: 'Sucursal de alertas' })).toBeDisabled()
  const calls = fake.fetcher.mock.calls.map(([p]) => String(p)); expect(calls.filter(p => p.includes('/notifications')).every(p => p.includes('branchId=' + workspace.branchId))).toBe(true); expect(calls.some(p => p.includes('/branches?'))).toBe(false)
})
it('paginates and filters without treating a failed read as empty', async () => {
  const fake = managementServer(); fake.management.notifications = Array.from({ length: 21 }, (_, index) => ({ ...alert, notificationId: id(550 + index), title: 'Alerta ' + index })); fake.management.totalCount = n('21'); mountTransfers(fake, <NotificationsPage />)
  await screen.findByText('Alerta 0'); fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' })); await screen.findByText('Alerta 20'); expect(screen.queryByText('Alerta 0')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('checkbox', { name: 'Solo no leídas' })); await screen.findByText('Alerta 0')
  fake.management.failGet = true; fireEvent.click(screen.getByRole('button', { name: 'Actualizar consulta' })); await screen.findAllByRole('alert'); expect(screen.queryByText('No hay notificaciones en esta página.')).not.toBeInTheDocument()
})
it('keeps a lost read pending after reload, and reconciles only an observed read state', async () => {
  const fake = managementServer(); fake.management.notifications = [{ ...alert, isRead: true, readAt: alert.createdAt }]
  mountTransfers(fake, <NotificationsPage />, '?notificationRead=' + alert.notificationId)
  fireEvent.click(await screen.findByRole('button', { name: 'Conciliar lectura registrada' })); await waitFor(() => expect(screen.getByTestId('location')).not.toHaveTextContent('notificationRead')); expect(fake.writes()).toHaveLength(0)
})
it('cancels old requests and hides late private data on membership change', async () => {
  const fake = managementServer(); let finish!: (value: Response) => void; fake.management.delay = new Promise(resolve => { finish = resolve }); const view = mountTransfers(fake, <NotificationsPage />)
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([p]) => String(p).includes('/notifications?'))).toBe(true)); const signal = fake.fetcher.mock.calls.find(([p]) => String(p).includes('/notifications?'))![1]!.signal!
  fake.management.delay = null; fake.management.notifications = []; fake.management.totalCount = n('0'); view.changeContext({ ...workspace, session: { userId: id(902) }, access: { ...workspace.access, membershipId: id(903) } }); expect(signal.aborted).toBe(true)
  await act(async () => finish(json({ rows: [{ ...alert, title: 'LATE-PRIVATE-ALERT' }], totalCount: n('1') }))); expect(screen.queryByText('LATE-PRIVATE-ALERT')).not.toBeInTheDocument(); await screen.findByText('No hay notificaciones en esta página.')
})
it('resets an Owner all-branch filter when the same membership becomes a worker', async () => {
  const fake = managementServer(); const view = mountTransfers(fake, <NotificationsPage />); await screen.findByRole('option', { name: 'Todas las sucursales autorizadas' }); fireEvent.change(screen.getByRole('combobox', { name: 'Sucursal de alertas' }), { target: { value: '' } }); await screen.findByText('Total del filtro: 1')
  const before = fake.fetcher.mock.calls.length; view.changeContext({ ...workspace, access: { ...workspace.access, role: 'cashier' } }); await screen.findByText('Total del filtro: 1'); expect(screen.getByRole('combobox', { name: 'Sucursal de alertas' })).toHaveValue(workspace.branchId); expect(screen.getByRole('combobox', { name: 'Sucursal de alertas' })).toBeDisabled()
  expect(fake.fetcher.mock.calls.slice(before).filter(([p]) => String(p).includes('/notifications')).every(([p]) => String(p).includes('branchId=' + workspace.branchId))).toBe(true)
})
