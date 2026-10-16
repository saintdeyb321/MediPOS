import { screen, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { ArrowLeftRight } from 'lucide-react'
import { WorkspaceNavigation } from '../../app/layouts/WorkspaceNavigation'
import { DashboardPage } from '../dashboard'
import { mountTransfers, transferServer, workspace } from './transfers.test.support'

it('exposes the real transfers route without inheriting an unavailable label in the workspace navigation', () => {
  const fake = transferServer(); mountTransfers(fake, <WorkspaceNavigation workspace={workspace} />)
  const link = screen.getByRole('link', { name: 'Transferencias' }); expect(link).toHaveAttribute('href', workspace.base + '/transferencias'); expect(link).not.toHaveAttribute('title')
  expect(within(link).queryByText('Pronto')).not.toBeInTheDocument(); expect(screen.getByRole('link', { name: 'Reportes' })).not.toHaveAttribute('title'); expect(fake.writes()).toHaveLength(0)
})
it('describes the transfers shortcut as operational without enabling a future reporting module', () => {
  const fake = transferServer(); mountTransfers(fake, <DashboardPage shortcuts={[{ id: 'transferencias', label: 'Transferencias', description: 'Mercadería entre sucursales', icon: ArrowLeftRight }]} />)
  const link = screen.getByRole('link', { name: /^Transferencias/ }); expect(within(link).getByText('Consultar y operar')).toBeInTheDocument(); expect(within(link).queryByText('Próximamente')).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
