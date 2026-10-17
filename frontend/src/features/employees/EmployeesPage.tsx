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
  const key = scopedKey(w, 'employees', w.session.userId, w.access.membershipId, offset)
  const employees = useQuery({ queryKey: key, queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return employeesApi(http).list(w, offset, 20, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const branches = useQuery({ queryKey: scopedKey(w, 'employee-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const complete = branches.isSuccess && !branches.isError && branches.data.length < 100
  const employee = !employees.isError ? employees.data?.find(row => sameId(row.membershipId, selected)) : undefined
  function select(id: string) { setReviewed(false); setParams(current => { const next = new URLSearchParams(current); next.set('employee', id); return next }, { replace: true }) }
  function page(value: number) { setReviewed(false); setParams(current => { const next = new URLSearchParams(current); next.set('employeePage', String(value)); if (!write.pending) next.delete('employee'); return next }, { replace: true }) }
  return <div className="operations"><PageHeader title="Empleados" description={`${w.tenantName} · administración compartida entre sucursales. Solo Owner; cada cambio se vuelve a autorizar en el servidor.`} />
    {!http && <Notice tone="warning">Esta pantalla requiere conexión HTTPS real.</Notice>}
    <EmployeeInvitationsPanel />
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
      {employees.isFetching && <p role="status">Consultando empleados…</p>}
      {employees.isError && <ApiErrorState error={employees.error} title="No pudimos consultar empleados" />}
      {employees.data && !employees.isError && <div className="operation-table"><table><caption>Empleados consultados en el servidor · horarios de Lima</caption><thead><tr><th>Empleado</th><th>Rol / estado</th><th>Sucursales asignadas</th><th>Horarios</th><th>Registro UTC</th><th>Acción</th></tr></thead><tbody>{employees.data.map(row => <tr key={row.membershipId}>
        <td>{row.displayName}{sameId(row.membershipId, w.access.membershipId) && <strong> · Tu membresía actual</strong>}</td><td>{roleLabels[row.role]} / {row.isActive ? 'Activo' : 'Inactivo'}</td>
        <td>{row.branchIds.length ? <ul>{row.branchIds.map(id => <li key={id}>{branches.data?.find(branch => sameId(branch.id, id))?.name ?? 'Sucursal no disponible en la consulta'}</li>)}</ul> : 'Sin asignaciones registradas'}</td>
        <td>{row.schedule.length ? <ul>{row.schedule.map(window => <li key={windowLabel(window)}>{windowLabel(window)}</li>)}</ul> : 'Sin ventanas configuradas'}</td>
        <td><time dateTime={row.createdAt}>{row.createdAt}</time></td><td><Button variant="quiet" disabled={write.busy || Boolean(write.pending && !sameId(row.membershipId, write.targetId))} onClick={() => select(row.membershipId)}>Gestionar {row.displayName}</Button></td>
      </tr>)}</tbody></table>{!employees.data.length && <p>No hay empleados en esta página. No se crearon identidades.</p>}</div>}
      <PageControls offset={offset} count={employees.isError ? undefined : employees.data?.length} busy={employees.isFetching || write.busy || Boolean(write.pending)} change={page} refresh={() => { setReviewed(false); void employees.refetch() }} />
    </div></Panel>
    {employee && <Panel><div className="operation-body" key={JSON.stringify(employee)}><h2>Accesos de {employee.displayName}</h2><p>Rol: {roleLabels[employee.role]} · {employee.isActive ? 'Activo' : 'Inactivo'}. Los controles no conceden autoridad de plataforma.</p>
      {sameId(employee.userId, w.session.userId) && <Notice>Estás modificando tu propia membresía. Se cancelará el contexto almacenado y se volverá a verificar el acceso antes de continuar operando.</Notice>}
      <EmployeeBranchesForm employee={employee} branches={branches.data ?? []} complete={complete} write={write} />
      <EmployeeScheduleForm employee={employee} write={write} /><EmployeeDeactivation employee={employee} write={write} />
    </div></Panel>}
    {selected && employees.isSuccess && !employee && <p>No se encontró el empleado seleccionado en esta página. Consulta la página original; su ausencia no demuestra una desactivación ni permite repetir una operación.</p>}
  </div>
}
