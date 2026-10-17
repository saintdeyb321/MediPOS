import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import type { QueryClient } from '@tanstack/react-query'
import { expect, it } from 'vitest'
import { EmployeesPage } from './EmployeesPage'
import { employeeServer, id, json, mountEmployees, owner, worker, workspace } from './employees.test.support'

async function manage(name = worker.displayName) { fireEvent.click(await screen.findByRole('button', { name: 'Gestionar ' + name })); return screen.findByRole('heading', { name: 'Accesos de ' + name }) }
async function saveBranches() {
  const form = within(screen.getByRole('form', { name: 'Asignar sucursales' }))
  const second = form.getByRole('checkbox', { name: 'Otra sede unitaria' }); await waitFor(() => expect(second).toBeEnabled()); fireEvent.click(second)
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé y autorizo reemplazar/ })); const save = form.getByRole('button', { name: 'Guardar sucursales' }); fireEvent.click(save); return save
}
function CacheProbe({ capture }: { capture: (client: QueryClient) => void }) { const client = useQueryClient(); useEffect(() => capture(client), [capture, client]); return null }

it('lists names, roles, state, assigned branches, schedules and registration from the server without creating users', async () => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />)
  const row = within(await screen.findByRole('row', { name: /Trabajador unitario/ }))
  expect(row.getByRole('cell', { name: 'Químico farmacéutico / Activo' })).toBeInTheDocument()
  await row.findByText('Sede unitaria'); expect(row.getByText('Lunes 09:00:00–18:00:00')).toBeInTheDocument()
  expect(screen.getByRole('heading', { name: 'Invitaciones de empleados' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Crear invitación' })).toBeDisabled()
  expect(screen.queryByRole('textbox', { name: /correo|UUID|UserId/i })).not.toBeInTheDocument()
  expect(fake.writes()).toHaveLength(0)
})
it('paginates on the server and distinguishes empty pages from failed queries', async () => {
  const fake = employeeServer(); fake.state.employees = Array.from({ length: 21 }, (_, index) => ({ ...worker, membershipId: id(100 + index), userId: id(200 + index), displayName: 'Empleado ' + index }))
  const view = mountEmployees(fake, <EmployeesPage />)
  await screen.findByRole('button', { name: 'Gestionar Empleado 0' })
  const next = screen.getAllByRole('button', { name: 'Página siguiente' }).find(button => !button.hasAttribute('disabled'))!
  expect(next).toBeEnabled(); fireEvent.click(next)
  await screen.findByRole('button', { name: 'Gestionar Empleado 20' }); expect(screen.queryByRole('button', { name: 'Gestionar Empleado 0' })).not.toBeInTheDocument()
  expect(fake.fetcher.mock.calls.some(([path]) => String(path).endsWith('/employees?offset=20&limit=20'))).toBe(true)
  view.unmount(); fake.state.employees = []; const empty = mountEmployees(fake, <EmployeesPage />)
  await screen.findByText(/No hay empleados en esta página/); empty.unmount(); fake.state.failList = true; mountEmployees(fake, <EmployeesPage />)
  await screen.findByText('No pudimos consultar empleados'); expect(screen.queryByText(/No hay empleados en esta página/)).not.toBeInTheDocument()
})
it.each(['pharmacist', 'cashier'] as const)('denies %s before querying or exposing employee administration', role => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />, '', { ...workspace, access: { ...workspace.access, role } })
  expect(screen.getByText(/Administrar empleados requiere Owner/)).toBeInTheDocument(); expect(fake.fetcher).not.toHaveBeenCalled()
})
it('replaces the complete branch set using 204, checks review and rereads after one write', async () => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Asignar sucursales' }))
  expect(form.getByRole('checkbox', { name: 'Sede unitaria' })).toBeChecked(); expect(form.getByRole('button', { name: 'Guardar sucursales' })).toBeDisabled()
  const button = await saveBranches(); fireEvent.click(button)
  await screen.findByText(/Cambio guardado y empleado consultado nuevamente/)
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toBe(`{"branchIds":["${id(5)}","${id(6)}"]}`)
  expect(fake.fetcher.mock.calls.filter(([path, init]) => init?.method === 'GET' && String(path).includes('/employees?')).length).toBeGreaterThanOrEqual(2)
  expect(within(screen.getByRole('form', { name: 'Asignar sucursales' })).getByRole('checkbox', { name: 'Otra sede unitaria' })).toBeChecked()
})
it('never erases an unconsultable assignment or invents branch names when lookup fails', async () => {
  const fake = employeeServer(); fake.state.employees[1]!.branchIds = [id(99)]
  const view = mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Asignar sucursales' }))
  expect(form.getByText(/resolver una asignación existente/)).toBeInTheDocument(); expect(form.getByRole('button', { name: 'Guardar sucursales' })).toBeDisabled()
  view.unmount(); fake.state.failBranches = true; mountEmployees(fake, <EmployeesPage />); await manage()
  await screen.findByText('No pudimos consultar las sucursales'); expect(screen.getByRole('button', { name: 'Guardar sucursales' })).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
})
it('refuses a foreign-tenant branch response and leaves the full replacement disabled', async () => {
  const fake = employeeServer(); fake.state.branches = [{ ...fake.state.branches[0]!, tenantId: id(99) }]
  mountEmployees(fake, <EmployeesPage />); await manage(); await screen.findByText('No pudimos consultar las sucursales')
  expect(screen.getByRole('button', { name: 'Guardar sucursales' })).toBeDisabled(); expect(fake.writes()).toHaveLength(0)
})
it('edits multiple time windows with named day and exact microseconds, rejecting duplicates and midnight crossings', async () => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Configurar horario' }))
  fireEvent.change(form.getByRole('combobox', { name: 'Día de la semana' }), { target: { value: 'tuesday' } })
  fireEvent.change(form.getByRole('textbox', { name: 'Hora de inicio' }), { target: { value: '09:00:00.000001' } }); fireEvent.change(form.getByRole('textbox', { name: 'Hora de fin' }), { target: { value: '12:00' } }); fireEvent.click(form.getByRole('button', { name: 'Agregar ventana al horario' }))
  expect(form.getByText('Martes 09:00:00.000001–12:00:00')).toBeInTheDocument()
  fireEvent.change(form.getByRole('textbox', { name: 'Hora de inicio' }), { target: { value: '09:00:00.000001' } }); fireEvent.change(form.getByRole('textbox', { name: 'Hora de fin' }), { target: { value: '12:00' } })
  expect(form.getByRole('button', { name: 'Agregar ventana al horario' })).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: 'Hora de inicio' }), { target: { value: '23:00' } }); fireEvent.change(form.getByRole('textbox', { name: 'Hora de fin' }), { target: { value: '01:00' } })
  expect(form.getByRole('button', { name: 'Agregar ventana al horario' })).toBeDisabled()
  fireEvent.change(form.getByRole('textbox', { name: 'Hora de inicio' }), { target: { value: '' } }); fireEvent.change(form.getByRole('textbox', { name: 'Hora de fin' }), { target: { value: '' } })
  fireEvent.click(form.getByRole('checkbox', { name: /Revisé y autorizo reemplazar el horario/ })); fireEvent.click(form.getByRole('button', { name: 'Guardar horario' }))
  await screen.findByText(/Cambio guardado y empleado consultado nuevamente/)
  expect(fake.writes()).toHaveLength(1); expect(fake.writes()[0]?.[1]?.body).toContain('"dayOfWeek":"tuesday","startTime":"09:00:00.000001","endTime":"12:00:00"')
})
it('requires explicit review for an empty schedule and never labels it as permanent access', async () => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Configurar horario' }))
  fireEvent.click(form.getByRole('button', { name: 'Quitar ventana Lunes 09:00:00–18:00:00' }))
  expect(form.getByText(/Horario preparado vacío: no es acceso permanente/)).toBeInTheDocument(); expect(form.getByRole('button', { name: 'Guardar horario' })).toBeDisabled()
  fireEvent.click(form.getByRole('checkbox', { name: /por un conjunto vacío/ })); fireEvent.click(form.getByRole('button', { name: 'Guardar horario' }))
  await screen.findByText(/Cambio guardado y empleado consultado nuevamente/); expect(fake.writes()[0]?.[1]?.body).toBe('{"windows":[]}')
})
it.each([1, 2])('blocks deactivating any Owner even with %s Owners visible, without trusting a paginated count', async count => {
  const fake = employeeServer(); fake.state.employees = count === 1 ? [owner] : [owner, { ...owner, userId: id(90), membershipId: id(91), displayName: 'Otro Owner unitario' }]
  mountEmployees(fake, <EmployeesPage />); await manage(owner.displayName)
  expect(screen.getByRole('button', { name: 'Desactivar Owner · bloqueado' })).toBeDisabled(); expect(screen.queryByRole('button', { name: 'Desactivar empleado una vez' })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(0)
})
it('deactivates an active non-Owner only after explicit review, then rereads without deleting history', async () => {
  const fake = employeeServer(); mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Desactivar empleado' })); const button = form.getByRole('button', { name: 'Desactivar empleado una vez' }); expect(button).toBeDisabled()
  fireEvent.click(form.getByRole('checkbox')); fireEvent.click(button); fireEvent.click(button)
  await screen.findByText(/Esta membresía está inactiva/); expect(fake.writes()).toHaveLength(1); expect(fake.state.employees).toHaveLength(2)
  expect(screen.queryByRole('form', { name: 'Desactivar empleado' })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Guardar horario' })).toBeDisabled()
})
it('keeps lost branch writes blocked even if the read matches, including reload and consultation', async () => {
  const fake = employeeServer(); fake.state.loseBranches = true; const view = mountEmployees(fake, <EmployeesPage />); await manage(); await saveBranches()
  await screen.findByRole('heading', { name: 'Cambio pendiente de conciliación' })
  await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar empleado de la operación' })).toBeEnabled()); fireEvent.click(screen.getByRole('button', { name: 'Consultar empleado de la operación' }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Continuar tras conciliación verificada' })).toBeDisabled())
  expect(screen.getByTestId('location').textContent).toContain('employeePending=branches'); expect(fake.writes()).toHaveLength(1)
  view.unmount(); mountEmployees(fake, <EmployeesPage />, `?employee=${id(20)}&employeePage=0&employeePending=branches`)
  await screen.findByRole('heading', { name: 'Accesos de ' + worker.displayName }); expect(screen.getByRole('button', { name: 'Guardar sucursales' })).toBeDisabled(); expect(fake.writes()).toHaveLength(1)
})
it('requires verified reconciliation to unlock an uncertain schedule rather than retrying it', async () => {
  const fake = employeeServer(); fake.state.loseSchedule = true; mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Configurar horario' })); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Guardar horario' }))
  await screen.findByRole('heading', { name: 'Cambio pendiente de conciliación' })
  const acknowledge = await screen.findByRole('button', { name: 'Continuar tras conciliación verificada' }); expect(acknowledge).toBeDisabled()
  fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué el empleado y el conjunto original/ })); fireEvent.click(acknowledge)
  await waitFor(() => expect(screen.getByTestId('location').textContent).not.toContain('employeePending'))
  expect(fake.writes()).toHaveLength(1)
})
it.each([true, false])('reconciles a lost deactivation only when the same membership is inactive (active=%s)', async active => {
  const fake = employeeServer(); fake.state.loseDeactivate = true; fake.state.remainActive = active; mountEmployees(fake, <EmployeesPage />); await manage()
  const form = within(screen.getByRole('form', { name: 'Desactivar empleado' })); fireEvent.click(form.getByRole('checkbox')); fireEvent.click(form.getByRole('button', { name: 'Desactivar empleado una vez' }))
  if (active) {
    await screen.findByRole('heading', { name: 'Cambio pendiente de conciliación' }); await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar empleado de la operación' })).toBeEnabled()); fireEvent.click(screen.getByRole('button', { name: 'Consultar empleado de la operación' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Consultar empleado de la operación' })).toBeEnabled()); expect(screen.getByRole('button', { name: 'Desactivar empleado una vez' })).toBeDisabled()
    expect(screen.getByTestId('location').textContent).toContain('employeePending=deactivate')
  } else { await screen.findByText(/Esta membresía está inactiva/); await waitFor(() => expect(screen.getByTestId('location').textContent).not.toContain('employeePending')) }
  expect(fake.writes()).toHaveLength(1)
})
it('resets own operational context and tenant caches before permitting further operations', async () => {
  const fake = employeeServer(); let client!: QueryClient
  const accessKey = ['tenant', id(2), id(5), 'access', '/empleados']
  const capture = (value: QueryClient) => { client = value; client.setQueryData(accessKey, { isAllowed: true }); client.setQueryData(['tenant', id(2), id(5), 'product-search'], { private: true }); client.setQueryData(['tenant', id(99), id(5), 'access'], { isAllowed: true }) }
  mountEmployees(fake, <><EmployeesPage /><CacheProbe capture={capture} /></>); await manage(owner.displayName); await saveBranches()
  await waitFor(() => expect(fake.writes()).toHaveLength(1))
  await waitFor(() => expect(client.getQueryData(accessKey)).toBeUndefined())
  expect(client.getQueryData(['tenant', id(2), id(5), 'product-search'])).toBeUndefined(); expect(client.getQueryData(['tenant', id(99), id(5), 'access'])).toEqual({ isAllowed: true })
})
it.each([400, 401, 403, 404, 409, 429])('shows HTTP %s without automatic retry or false administrative success', async status => {
  const fake = employeeServer(); fake.state.reject = status; mountEmployees(fake, <EmployeesPage />); await manage(); await saveBranches()
  await screen.findByText('No pudimos completar el cambio de acceso'); expect(fake.writes()).toHaveLength(1)
  expect(screen.queryByText(/Cambio guardado y empleado consultado nuevamente/)).not.toBeInTheDocument()
})
it('aborts an in-flight mutation on context change and ignores its late response', async () => {
  const fake = employeeServer(); let finish!: () => void; let signal: AbortSignal | null = null
  const normal = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, init) => {
    if (init?.method === 'PUT') { signal = init.signal as AbortSignal; await new Promise<void>(resolve => { finish = resolve }); return new Response(null, { status: 204 }) }
    if (String(path).includes('/tenants/' + id(99))) return json([])
    return normal(path, init)
  })
  const view = mountEmployees(fake, <EmployeesPage />); await manage(); await saveBranches(); await waitFor(() => expect(finish).toBeTypeOf('function'))
  view.changeContext({ ...workspace, tenantId: id(99), branchId: id(98), branch: { ...workspace.branch, tenantId: id(99), id: id(98) }, access: { ...workspace.access, tenantId: id(99), branchId: id(98) } })
  expect(signal!.aborted).toBe(true); await act(async () => { finish() })
  expect(screen.queryByText(/Cambio guardado y empleado consultado nuevamente/)).not.toBeInTheDocument(); expect(screen.queryByRole('heading', { name: 'Accesos de ' + worker.displayName })).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(1)
})
it('also retires own allowed access immediately when a self-write is cancelled by leaving the context', async () => {
  const fake = employeeServer(); let client!: QueryClient; let finish!: () => void
  const accessKey = ['tenant', id(2), id(5), 'access', '/empleados']
  const capture = (value: QueryClient) => { client = value; client.setQueryData(accessKey, { isAllowed: true }) }
  const normal = fake.fetcher.getMockImplementation()!
  fake.fetcher.mockImplementation(async (path, init) => {
    if (init?.method === 'PUT') { await new Promise<void>(resolve => { finish = resolve }); return new Response(null, { status: 204 }) }
    if (String(path).includes('/tenants/' + id(99))) return json([])
    return normal(path, init)
  })
  const view = mountEmployees(fake, <><EmployeesPage /><CacheProbe capture={capture} /></>); await manage(owner.displayName); await saveBranches(); await waitFor(() => expect(finish).toBeTypeOf('function'))
  view.changeContext({ ...workspace, tenantId: id(99), branchId: id(98), branch: { ...workspace.branch, tenantId: id(99), id: id(98) }, access: { ...workspace.access, tenantId: id(99), branchId: id(98) } })
  expect(client.getQueryData(accessKey)).toBeUndefined(); await act(async () => { finish() })
  expect(screen.queryByText(/Cambio guardado y empleado consultado nuevamente/)).not.toBeInTheDocument(); expect(fake.writes()).toHaveLength(1)
})
