import { act, screen, waitFor } from '@testing-library/react'
import { focusManager } from '@tanstack/react-query'
import { expect, it } from 'vitest'
import { OwnerDashboard } from './OwnerDashboard'
import { json, mountTransfers, reportingServer, workspace } from './reporting.test.support'

it('reads the daily dashboard once and displays exact metrics for the selected branch', async () => {
  const fake = reportingServer(); mountTransfers(fake, <OwnerDashboard />)
  await screen.findByText('Hoy en tu sucursal')
  await waitFor(() => expect(screen.getAllByText('S/ 9007199254740993.1234').length).toBeGreaterThan(0))
  const calls = fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/reports/'))
  expect(calls).toHaveLength(1)
  expect(String(calls[0]?.[0])).toContain(`/reports/dashboard?branchId=${workspace.branchId}&period=day`)
  expect(screen.getByRole('link', { name: 'Ver reportes' })).toHaveAttribute('href', `${workspace.base}/reportes`)
  expect(fake.writes()).toHaveLength(0)
})
it.each(['cashier', 'pharmacist'] as const)('does not query Owner metrics for %s', role => {
  const fake = reportingServer(); mountTransfers(fake, <OwnerDashboard />, '', { ...workspace, access: { ...workspace.access, role } })
  expect(fake.fetcher).not.toHaveBeenCalled(); expect(screen.queryByText('Hoy en tu sucursal')).not.toBeInTheDocument()
})
it('shows server failure without inventing zero metrics or retrying on focus', async () => {
  const fake = reportingServer(); const original = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation((path, init) => String(path).includes('/reports/') ? Promise.resolve(json({ code: 'unit.unavailable' }, 503)) : original(path, init))
  const view = mountTransfers(fake, <OwnerDashboard />)
  try {
    await screen.findByText('No pudimos consultar los indicadores')
    await act(async () => { focusManager.setFocused(false); focusManager.setFocused(true) })
    expect(fake.fetcher.mock.calls.filter(([path]) => String(path).includes('/reports/'))).toHaveLength(1)
    expect(screen.queryByText('S/ 0.00')).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
  } finally {
    view.unmount()
    focusManager.setFocused(undefined)
  }
})
