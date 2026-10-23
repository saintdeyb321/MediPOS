import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, roleLabels, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Panel } from '../../shared/ui/Panel'
import { Drawer, Modal } from '../../shared/ui/Modal'
import { PageToolbar } from '../../shared/ui/PageToolbar'
import { SearchField } from '../../shared/ui/TextField'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import { employeesApi, sameId } from './api'
import { windowLabel } from './schedule'
import { useEmployeeWrite } from './useEmployeeWrite'
import { EmployeeBranchesForm, EmployeeDeactivation, EmployeeScheduleForm } from './EmployeeForms'
import { EmployeeInvitationsPanel } from './EmployeeInvitationsPanel'
import '../../shared/styles/operations.css'

export function EmployeesPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Notice tone="warning">Administrar empleados requiere Owner con acceso vigente.</Notice>
  return <EmployeesWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function EmployeesWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const [params, setParams] = useSearchParams()
  const rawOffset = params.get('employeePage') ?? '0'
  const offset = /^\d{1,5}$/.test(rawOffset) && Number(rawOffset) <= 10000 && Number(rawOffset) % 20 === 0 ? Number(rawOffset) : 0
  const selected = params.get('employee') ?? ''; const write = useEmployeeWrite(offset)
  const [reviewed, setReviewed] = useState(false)
  const [invitationOpen, setInvitationOpen] = useState(false); const [drawerOpen, setDrawerOpen] = useState(false)
  const [tab, setTab] = useState<'profile' | 'branches' | 'schedule' | 'status'>('profile')
  const [task, setTask] = useState<'branches' | 'schedule' | 'status' | null>(null); const [dirty, setDirty] = useState(false)
  const [filter, setFilter] = useState('')
  const key = scopedKey(w, 'employees', w.session.userId, w.access.membershipId, offset)
  const employees = useQuery({ queryKey: key, queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return employeesApi(http).list(w, offset, 20, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const branches = useQuery({ queryKey: scopedKey(w, 'employee-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const employee = !employees.isError ? employees.data?.find(row => sameId(row.membershipId, selected)) : undefined
  function select(id: string) { setReviewed(false); setTab('profile'); setDrawerOpen(true); setParams(current => { const next = new URLSearchParams(current); next.set('employee', id); return next }, { replace: true }) }
  function page(value: number) { setReviewed(false); setParams(current => { const next = new URLSearchParams(current); next.set('employeePage', String(value)); if (!write.pending) next.delete('employee'); return next }, { replace: true }) }
  function edit(value: 'branches' | 'schedule' | 'status') { setDrawerOpen(false); setDirty(false); setTask(value) }
  return <div className="operations"><PageHeader title="Personal" description={`Equipo y accesos de ${w.tenantName}`} action={<Button disabled={!http || write.locked} onClick={() => setInvitationOpen(true)}>Nuevo integrante</Button>} />
    {!http && <Notice tone="warning">Esta pantalla requiere conexión HTTPS real.</Notice>}
    <EmployeeInvitationsPanel createOpen={invitationOpen} onCreateClose={() => setInvitationOpen(false)} onCreateOpen={() => setInvitationOpen(true)} />
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar el cambio de acceso" />}
    {write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><div className="operation-body"><h2>Cambio pendiente de conciliación</h2><p>No repitas el POST/PUT. Consulta el empleado original en su página. Una lectura temprana, incluso coincidente, no demuestra que una asignación u horario terminó ni que hubo rollback.</p>
      <Button variant="secondary" disabled={write.busy} onClick={() => { setReviewed(false); void write.consult() }}>Consultar empleado de la operación</Button>
      {write.observed && <><p>Estado consultado: {write.observed.displayName} · {roleLabels[write.observed.role]} · {write.observed.isActive ? 'Activo' : 'Inactivo'}. Revisa sucursales y ventanas completas en el detalle.</p>
        {write.pending !== 'deactivate' && employee && sameId(write.observed.membershipId, employee.membershipId) && <><label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué el empleado y el conjunto original solicitado, y concilié la finalización; no queda otra escritura en curso.</label>
          <Button variant="secondary" disabled={!reviewed || write.busy} onClick={() => { write.acknowledge(); setReviewed(false) }}>Continuar tras conciliación verificada</Button></>}
      </>}
      {write.pending === 'deactivate' && <p>Mientras la membresía siga activa no se repite la desactivación. Solo observarla inactiva permite verificar ese resultado terminal.</p>}
    </div></Panel>}
    {branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} title="No pudimos consultar las sucursales" />}
    <Panel><div className="operation-body"><h2>Equipo registrado</h2>
      <PageToolbar><SearchField label="Filtrar nombres de esta página" hint="No busca cuentas ni empleados fuera de esta página." value={filter} onChange={event => setFilter(event.target.value)} /></PageToolbar>
      {employees.isFetching && <p role="status">Consultando empleados…</p>}
      {employees.isError && <ApiErrorState error={employees.error} title="No pudimos consultar empleados" />}
      {employees.data && !employees.isError && <div className="operation-table"><table><caption>Personal de la botica · detalle de accesos por integrante</caption><thead><tr><th>Integrante</th><th>Rol / estado</th><th>Sucursales asignadas</th><th>Horarios</th><th>Acción</th></tr></thead><tbody>{employees.data.filter(row => row.displayName.toLocaleLowerCase('es-PE').includes(filter.toLocaleLowerCase('es-PE'))).map(row => <tr key={row.membershipId}>
        <td>{row.displayName}{sameId(row.membershipId, w.access.membershipId) && <strong> · Tú</strong>}</td><td>{roleLabels[row.role]}<br /><Badge tone={row.isActive ? 'success' : 'neutral'}>{row.isActive ? 'Activo' : 'Inactivo'}</Badge></td>
        <td>{row.branchIds.length ? <ul>{row.branchIds.map(id => <li key={id}>{branches.data?.find(branch => sameId(branch.id, id))?.name ?? 'Sucursal no disponible en la consulta'}</li>)}</ul> : 'Sin asignaciones registradas'}</td>
        <td>{row.schedule.length ? `${row.schedule.length} ventanas · ver detalle` : 'Sin horario configurado'}</td>
        <td><Button variant="quiet" disabled={write.busy || Boolean(write.pending && !sameId(row.membershipId, write.targetId))} onClick={() => select(row.membershipId)}>Ver {row.displayName}</Button></td>
      </tr>)}</tbody></table>{!employees.data.length && <p>No hay empleados en esta página. No se crearon identidades.</p>}</div>}
      <PageControls offset={offset} count={employees.isError ? undefined : employees.data?.length} busy={employees.isFetching || write.busy || Boolean(write.pending)} change={page} refresh={() => { setReviewed(false); void employees.refetch() }} />
    </div></Panel>
    <Drawer open={drawerOpen && Boolean(employee)} title={employee ? employee.displayName : 'Detalle del integrante'} onClose={() => setDrawerOpen(false)}>
    {employee && <div className="operation-body"><p>{roleLabels[employee.role]} · {employee.isActive ? 'Activo' : 'Inactivo'}</p>
      <nav className="task-tabs" aria-label="Ficha del integrante">{([['profile', 'Perfil'], ['branches', 'Sucursales'], ['schedule', 'Horarios'], ['status', 'Estado']] as const).map(([id, label]) => <Button key={id} variant="quiet" aria-pressed={tab === id} onClick={() => setTab(id)}>{label}</Button>)}</nav>
      {sameId(employee.userId, w.session.userId) && <Notice>Estás modificando tu propia membresía. Se cancelará el contexto almacenado y se volverá a verificar el acceso antes de continuar operando.</Notice>}
      {tab === 'profile' && <><h3>Cuenta registrada con Google</h3><p>Integrante desde <DateText value={employee.createdAt} />.</p><p>El rol y las asignaciones se verifican en cada operación. No conceden autoridad de plataforma.</p></>}
      {tab === 'branches' && <><ul>{employee.branchIds.map(id => <li key={id}>{branches.data?.find(branch => sameId(branch.id, id))?.name ?? 'Sucursal no disponible en la consulta'}</li>)}</ul><Button disabled={write.locked || !employee.isActive} onClick={() => edit('branches')}>Editar sucursales</Button></>}
      {tab === 'schedule' && <><h3>Horario en Lima</h3>{employee.schedule.length ? <ul>{employee.schedule.map(window => <li key={windowLabel(window)}>{windowLabel(window)}</li>)}</ul> : <p>Sin horario configurado. Un horario vacío no habilita operaciones a trabajadores.</p>}<Button disabled={write.locked || !employee.isActive} onClick={() => edit('schedule')}>Editar horario</Button></>}
      {tab === 'status' && <><p>La desactivación conserva el historial y afecta las nuevas operaciones autorizadas.</p><Button variant="secondary" disabled={write.locked} onClick={() => edit('status')}>Revisar estado y desactivación</Button></>}
    </div>}
    </Drawer>
    <Modal open={task !== null} title={task === 'branches' ? 'Asignar sucursales' : task === 'schedule' ? 'Configurar horario' : 'Estado del integrante'} onClose={() => { setTask(null); setDirty(false) }} dirty={dirty || task === 'schedule'} busy={write.busy} recovery={Boolean(write.pending)} wide={task === 'schedule'}>
      <div className="operation-body" onChangeCapture={() => setDirty(true)}>
        {write.error != null && <ApiErrorState error={write.error} />}{write.notice && <p role="status">{write.notice}</p>}
        {!employee && <Notice tone="warning">El integrante no está disponible en esta consulta. Conserva la operación original y vuelve a consultarlo antes de editar.</Notice>}
        {write.pending && <Notice tone="warning"><p>No repitas el cambio. Consulta la misma membresía y compara todas las asignaciones y ventanas.</p><Button disabled={write.busy} onClick={() => { setReviewed(false); void write.consult() }}>Consultar integrante original</Button>
          {write.observed && <><p>{write.observed.displayName} · {roleLabels[write.observed.role]} · {write.observed.isActive ? 'Activo' : 'Inactivo'}</p>
            <ul>{write.observed.branchIds.map(id => <li key={id}>{branches.data?.find(branch => sameId(branch.id, id))?.name ?? 'Sucursal no disponible en la consulta'}</li>)}</ul>
            <ul>{write.observed.schedule.map(window => <li key={windowLabel(window)}>{windowLabel(window)}</li>)}</ul>
            {write.pending !== 'deactivate' && employee && sameId(write.observed.membershipId, employee.membershipId) && <><label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué el conjunto solicitado y la finalización, sin otra escritura en curso.</label><Button disabled={!reviewed || write.busy} onClick={() => { write.acknowledge(); setReviewed(false) }}>Continuar tras conciliación verificada</Button></>}
          </>}
        </Notice>}
        {employee && <div key={JSON.stringify(employee)}>
          {sameId(employee.userId, w.session.userId) && <Notice>Modificar tu propia membresía obliga a volver a verificar el acceso.</Notice>}
          {task === 'branches' && <EmployeeBranchesForm employee={employee} branches={branches.data ?? []} complete={complete} write={write} />}
          {task === 'schedule' && <EmployeeScheduleForm employee={employee} write={write} />}
          {task === 'status' && <EmployeeDeactivation employee={employee} write={write} />}
        </div>}
      </div>
    </Modal>
    {selected && employees.isSuccess && !employee && <p>No se encontró el empleado seleccionado en esta página. Consulta la página original; su ausencia no demuestra una desactivación ni permite repetir una operación.</p>}
  </div>
}
