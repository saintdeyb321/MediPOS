import { fireEvent, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { expect, it } from 'vitest'
import { EmployeesPage } from './EmployeesPage'
import { AcceptInvitationPage } from './AcceptInvitationPage'
import { employeeServer, id, json, mountEmployees, time } from './employees.test.support'

function server() {
  const fake = employeeServer(); const original = fake.fetcher.getMockImplementation()!
  const row = { id: id(80), tenantId: id(2), clientRequestId: id(81), role: 'cashier', status: 'accepted_pending_approval', version: 17, createdAt: time, expiresAt: '2026-10-16T12:15:00Z', acceptedAt: time, acceptedDisplayName: 'Cuenta aceptante unitaria', acceptedEmail: 'worker@example.test', approvedAt: null as string | null, resultMembershipId: null as string | null, revokedAt: null as string | null, revocationReason: null as string | null, canApprove: true }
  let lost = false; let exists = true
  fake.fetcher.mockImplementation(async (input, init) => {
    const path = String(input); const method = init?.method ?? 'GET'
    if (path === '/auth/me') return json({ userId: id(1) })
    if (method === 'GET' && path.includes('/employee-invitations?')) return json(exists ? [row] : [])
    if (method === 'GET' && path.endsWith('/employee-invitations/' + id(80))) return json(row)
    if (method === 'POST' && path.endsWith('/employee-invitations')) {
      const data = JSON.parse(String(init?.body)) as { clientRequestId: string; role: string }; row.clientRequestId = data.clientRequestId; row.role = data.role; exists = true
      if (lost) throw new TypeError('Synthetic response loss')
      return json({ invitation: row, ticket: 'unit-protected-ticket' }, 201)
    }
    if (method === 'POST' && path.endsWith('/approve')) { row.status = 'approved'; row.resultMembershipId = id(90); row.canApprove = false; row.approvedAt = time; row.version++; return json(row) }
    if (method === 'POST' && path === '/api/employee-invitations/accept') return json({ status: 'accepted_pending_approval', expiresAt: row.expiresAt })
    return original(input, init)
  })
  return { ...fake, row, lose: () => { lost = true; exists = false } }
}
it('requires independent identity review and an explicit separate approval; double clicks send one versioned write', async () => {
  const fake = server(); mountEmployees(fake, <EmployeesPage />)
  await screen.findByText(/Cuenta que aceptó: Cuenta aceptante unitaria/)
  const button = screen.getByRole('button', { name: 'Aprobar incorporación' }); expect(button).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué de forma independiente/ })); fireEvent.click(button); fireEvent.click(button)
  await screen.findByText(/Membresía creada por aprobación/)
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toBe('{"expectedVersion":17,"confirmed":true}')
})
it('shows the protected link once in memory and sends no arbitrary account identifier', async () => {
  const fake = server(); mountEmployees(fake, <EmployeesPage />)
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé el rol/ })); fireEvent.click(screen.getByRole('button', { name: 'Crear invitación' }))
  const link = await screen.findByRole('textbox', { name: /Enlace temporal/ }); expect(link).toHaveValue('https://medipos.test/aceptar-invitacion#unit-protected-ticket')
  expect(fake.writes()[0]?.[1]?.body).not.toMatch(/userId|email|subject/i)
  fireEvent.click(screen.getByRole('button', { name: 'Ocultar enlace' })); expect(screen.queryByRole('textbox', { name: /Enlace temporal/ })).not.toBeInTheDocument()
})
it('reconciles a lost creation through the original intention without issuing another secret or another POST', async () => {
  const fake = server(); fake.lose(); mountEmployees(fake, <EmployeesPage />)
  fireEvent.click(screen.getByRole('checkbox', { name: /Revisé el rol/ })); fireEvent.click(screen.getByRole('button', { name: 'Crear invitación' }))
  const read = await screen.findByRole('button', { name: 'Consultar invitación original' }); fireEvent.click(read)
  await screen.findByText(/Intención original localizada/)
  expect(fake.writes()).toHaveLength(1); expect(screen.queryByRole('textbox', { name: /Enlace temporal/ })).not.toBeInTheDocument()
})
it('after reload reconciles another terminal transition without claiming its own approval won', async () => {
  const fake = server(); fake.row.status = 'revoked'; fake.row.canApprove = false; fake.row.revokedAt = time; fake.row.revocationReason = 'Unit concurrent revocation'
  mountEmployees(fake, <EmployeesPage />, `?invitationTarget=${id(80)}&invitationAction=approve`)
  fireEvent.click(await screen.findByRole('button', { name: 'Consultar invitación original' }))
  await screen.findByText(/otro cambio concurrente/); expect(fake.writes()).toHaveLength(0)
})
it('removes the fragment in StrictMode and accepts only the current session after explicit consent', async () => {
  window.history.replaceState(null, '', '/aceptar-invitacion#unit-protected-ticket')
  const fake = server(); mountEmployees(fake, <StrictMode><AcceptInvitationPage /></StrictMode>)
  const consent = await screen.findByRole('checkbox', { name: /Confirmo que esta es mi sesión/ })
  expect(window.location.hash).toBe(''); const button = screen.getByRole('button', { name: 'Aceptar invitación' }); expect(button).toBeDisabled()
  fireEvent.click(consent); fireEvent.click(button); fireEvent.click(button)
  await waitFor(() => expect(screen.getByText(/Aceptación registrada/)).toBeInTheDocument())
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toBe('{"ticket":"unit-protected-ticket"}')
  window.history.replaceState(null, '', '/')
})
