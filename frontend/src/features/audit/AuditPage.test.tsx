import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { expect, it } from 'vitest'
import { AuditPage } from './AuditPage'
import { validPeriod } from './api'
import { id, managementServer, mountTransfers, workspace } from '../notifications/management.test.support'

async function auditDetail() {
  fireEvent.click((await screen.findAllByRole('button', { name: 'Ver evento' }))[0]!)
  const detail = within(await screen.findByRole('dialog', { name: 'Detalle del evento de auditoría' }))
  fireEvent.click(detail.getByText('Identificadores para soporte'))
  return detail
}

it.each(['pharmacist', 'cashier'] as const)('denies audit to %s without requesting private metadata', role => {
  const fake = managementServer(); mountTransfers(fake, <AuditPage />, '', { ...workspace, access: { ...workspace.access, role } }); expect(screen.getByText('Auditoría requiere Owner.')).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('shows actor/action/entity/correlation and only the presence of internal snapshots', async () => {
  const fake = managementServer(); mountTransfers(fake, <AuditPage />); const detail = await auditDetail()
  expect(detail.getByText('unit.action')).toBeInTheDocument(); expect(detail.getByText('unit-correlation')).toBeInTheDocument(); expect(detail.getByText(workspace.session.userId)).toBeInTheDocument()
  expect(detail.getByText('Estado anterior: No registrado. Estado posterior: Registrado.')).toBeInTheDocument(); expect(screen.queryByText('CONFIDENTIAL-UNIT-PAYLOAD')).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('validates the exact UTC 31-day limit, entity UUID, order and explicit filter application', async () => {
  expect(validPeriod({ fromUtc: '2026-10-01T00:00:00Z', toUtc: '2026-11-01T00:00:00Z', entityId: '', offset: 0 })).toBe(true)
  for (const toUtc of ['2026-11-01T00:00:01Z', '2026-10-01T00:00:00Z', '2026-10-02T00:00:00-05:00']) expect(validPeriod({ fromUtc: '2026-10-01T00:00:00Z', toUtc, entityId: '', offset: 0 })).toBe(false)
  const fake = managementServer(); mountTransfers(fake, <AuditPage />); await screen.findByText('unit.action'); const before = fake.fetcher.mock.calls.length
  fireEvent.change(screen.getByLabelText('Desde UTC'), { target: { value: '2026-10-01T00:00' } }); fireEvent.change(screen.getByLabelText('Hasta UTC'), { target: { value: '2026-11-01T00:01' } }); expect(screen.getByRole('button', { name: 'Consultar historial' })).toBeDisabled(); expect(fake.fetcher.mock.calls).toHaveLength(before)
  fireEvent.change(screen.getByLabelText('Hasta UTC'), { target: { value: '2026-10-02T00:00' } }); fireEvent.change(screen.getByRole('textbox', { name: 'Identificador de entidad (opcional)' }), { target: { value: id(4) } }); fireEvent.click(screen.getByRole('button', { name: 'Consultar historial' })); await waitFor(() => expect(fake.fetcher.mock.calls.some(([p]) => String(p).includes('fromUtc=2026-10-01T00%3A00%3A00Z') && String(p).includes('entityId=' + id(4)))).toBe(true))
})
it('uses server paging, empty and error states, without hiding failed data as empty', async () => {
  const fake = managementServer(); fake.management.audits = Array.from({ length: 21 }, (_, i) => ({ ...fake.management.audits[0]!, auditId: id(700 + i), correlationId: 'unit-page-' + i })); mountTransfers(fake, <AuditPage />)
  const first = await auditDetail(); expect(first.getByText('unit-page-0')).toBeInTheDocument(); fireEvent.click(first.getByRole('button', { name: /Cerrar/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Página siguiente' })); const next = await auditDetail()
  expect(next.getByText('unit-page-20')).toBeInTheDocument(); fireEvent.click(next.getByRole('button', { name: /Cerrar/ }))
  fake.management.failGet = true; fireEvent.click(screen.getByRole('button', { name: 'Actualizar consulta' })); await screen.findByRole('alert'); expect(screen.queryByText('No hay registros en esta página.')).not.toBeInTheDocument()
})
it('withdraws private audit metadata when an Owner loses their role', async () => {
  const fake = managementServer(); const view = mountTransfers(fake, <AuditPage />); const detail = await auditDetail(); expect(detail.getByText('unit-correlation')).toBeInTheDocument(); const count = fake.fetcher.mock.calls.length
  view.changeContext({ ...workspace, access: { ...workspace.access, role: 'cashier' } }); expect(screen.queryByText('unit-correlation')).not.toBeInTheDocument(); expect(screen.getByText('Auditoría requiere Owner.')).toBeInTheDocument(); expect(fake.fetcher.mock.calls).toHaveLength(count)
})
