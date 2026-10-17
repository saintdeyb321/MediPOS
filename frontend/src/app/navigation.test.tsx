import { screen, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { availableNavigation, navigation } from './navigation'
import { WorkspaceNavigation } from './layouts/WorkspaceNavigation'
import { DashboardPage } from '../features/dashboard'
import { ApiErrorState } from '../features/access'
import { ApiError } from '../shared/api/http'
import { availabilityLabels } from '../shared/ui/availability'
import { managementServer, mountTransfers, workspace } from '../features/notifications/management.test.support'

it.each(['owner', 'pharmacist', 'cashier'] as const)('shows implemented or partial modules for actual %s access without obsolete unavailable badges', role => {
  const w = { ...workspace, access: { ...workspace.access, role } }; const fake = managementServer(); mountTransfers(fake, <WorkspaceNavigation workspace={w} />, '', w)
  const nav = within(screen.getByRole('navigation', { name: 'Navegación principal' }))
  for (const item of navigation) {
    const accessibleName = item.availability === 'partial' ? item.label + ' · Parcial' : item.label
    if (item.roles.includes(role)) {
      const link = nav.getByRole('link', { name: accessibleName }); expect(link).toHaveAttribute('href', item.id ? w.base + '/' + item.id : w.base)
      if (item.availability === 'partial') expect(link).toHaveAttribute('title', item.description)
      else expect(link).not.toHaveAttribute('title')
    } else expect(nav.queryByRole('link', { name: accessibleName })).not.toBeInTheDocument()
  }
  expect(nav.queryByText(/Pronto|Próximamente/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('uses the same availability on dashboard shortcuts and distinguishes unstarted Offline without enabling it', () => {
  const fake = managementServer(); const shortcuts = availableNavigation('owner').filter(row => row.id && row.id !== 'ventas'); mountTransfers(fake, <DashboardPage shortcuts={shortcuts} />)
  for (const item of shortcuts) { const row = screen.getByRole('link', { name: new RegExp('^' + item.label) }); expect(within(row).getByText(availabilityLabels[item.availability])).toBeInTheDocument() }
  expect(screen.getByText(/Offline Sync: no iniciado/)).toBeInTheDocument(); expect(screen.queryByRole('link', { name: /Offline/ })).not.toBeInTheDocument(); expect(screen.getByText(/Implementado no significa aceptación comercial aprobada/)).toBeInTheDocument()
  expect(screen.queryByText(/Vista inicial · F1.1|habilitarán en F2|Próximamente/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('translates the new last-Owner error without revealing internal state or treating conflict as success', () => {
  const fake = managementServer(); mountTransfers(fake, <ApiErrorState error={new ApiError(409, 'membership.last_active_owner')} />); expect(screen.getByRole('alert')).toHaveTextContent('No se puede desactivar al último propietario activo de la botica.'); expect(fake.writes()).toHaveLength(0)
})
it('uses commercial language for selling and receiving without changing pending acceptance', () => {
  const fake = managementServer(); mountTransfers(fake, <DashboardPage shortcuts={availableNavigation('owner').filter(item => Boolean(item.id))} />)
  for (const id of ['ventas', 'compras']) expect(navigation.find(item => item.id === id)?.description).not.toMatch(/borrador/i)
  expect(screen.getByText(/imprimir el ticket interno o seguir vendiendo/)).toBeInTheDocument()
  expect(screen.getByText('Registrar recepción, revisar y confirmar mercadería')).toBeInTheDocument()
  expect(screen.getByText(/Implementado no significa aceptación comercial aprobada/)).toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
