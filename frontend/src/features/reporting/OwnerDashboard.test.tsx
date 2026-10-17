import { fireEvent, screen, waitFor } from '@testing-library/react'
import { expect, it } from 'vitest'
import { OwnerDashboard } from './OwnerDashboard'
import { json, mountTransfers, reportingServer, workspace } from './reporting.test.support'

it('reads both real contracts, displays exact metrics and applies the period only to dashboard', async () => {
  const fake = reportingServer(); mountTransfers(fake, <OwnerDashboard />)
  await screen.findByText('Indicadores reales de la sucursal')
  await waitFor(() => expect(screen.getAllByText('S/ 9007199254740993.1234').length).toBeGreaterThan(0))
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/overview?'))).toBe(true)
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/dashboard?'))).toBe(true)
  fireEvent.change(screen.getByRole('combobox', { name: 'Periodo del dashboard' }), { target: { value: 'week' } })
  await waitFor(() => expect(fake.fetcher.mock.calls.some(([path]) => String(path).includes('/reports/dashboard?') && String(path).endsWith('&period=week'))).toBe(true))
  expect(fake.writes()).toHaveLength(0)
})
it.each(['cashier', 'pharmacist'] as const)('does not query Owner metrics for %s', role => {
  const fake = reportingServer(); mountTransfers(fake, <OwnerDashboard />, '', { ...workspace, access: { ...workspace.access, role } })
  expect(fake.fetcher).not.toHaveBeenCalled(); expect(screen.queryByText('Indicadores reales de la sucursal')).not.toBeInTheDocument()
})
it('shows server failure without inventing zero metrics or retrying on focus', async () => {
  const fake = reportingServer(); const original = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation((path, init) => String(path).includes('/reports/') ? Promise.resolve(json({ code: 'unit.unavailable' }, 503)) : original(path, init))
  mountTransfers(fake, <OwnerDashboard />)
  await screen.findByText('No pudimos consultar dashboard operacional')
  expect(screen.queryByText('S/ 0.00')).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
