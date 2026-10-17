import { useId, useState } from 'react'
import type { Branch } from '../access'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import type { Employee } from './api'
import { canDeactivate, sameId } from './api'
import { dayLabels, days, timeInput, validSchedule, weekday, windowLabel } from './schedule'
import type { Weekday, WorkWindow } from './schedule'
import type { EmployeeWrite } from './useEmployeeWrite'

export function EmployeeBranchesForm({ employee, branches, complete, write }: { employee: Employee; branches: readonly Branch[]; complete: boolean; write: EmployeeWrite }) {
  const [ids, setIds] = useState([...employee.branchIds]); const [reviewed, setReviewed] = useState(false)
  const unresolved = employee.branchIds.some(id => !branches.some(branch => sameId(branch.id, id)))
  return <form aria-label="Asignar sucursales" onSubmit={event => { event.preventDefault(); if (complete && !unresolved && reviewed) void write.branches(employee, ids, branches.map(row => row.id)) }}>
    <h3>Sucursales asignadas</h3><p>Guardar reemplaza el conjunto completo. Una asignación vacía no habilita todas las sucursales. Owner conserva el alcance definido por su rol; no depende de estas asignaciones para administrar.</p>
    {(!complete || unresolved) && <p role="alert">Falta consultar todas las sucursales o resolver una asignación existente. No se enviará una lista incompleta ni se eliminarán asignaciones ocultas.</p>}
    <fieldset disabled={write.locked || !employee.isActive || !complete || unresolved}><legend>Sucursales consultables de esta botica</legend>
      {!branches.length && <p>No hay sucursales consultables.</p>}
      {branches.map(branch => <label key={branch.id}><input type="checkbox" checked={ids.some(id => sameId(id, branch.id))} onChange={event => { setIds(current => event.target.checked ? [...current, branch.id] : current.filter(id => !sameId(id, branch.id))); setReviewed(false) }} /> {branch.name}</label>)}
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé y autorizo reemplazar todas las asignaciones de este empleado.</label>
      <Button type="submit" disabled={!reviewed} pending={write.busy}>Guardar sucursales</Button>
    </fieldset>
  </form>
}
export function EmployeeScheduleForm({ employee, write }: { employee: Employee; write: EmployeeWrite }) {
  const [windows, setWindows] = useState<WorkWindow[]>(employee.schedule.map(row => ({ ...row })))
  const [day, setDay] = useState<Weekday>('monday'); const [start, setStart] = useState(''); const [end, setEnd] = useState(''); const [reviewed, setReviewed] = useState(false)
  const id = useId(); const from = timeInput(start); const to = timeInput(end)
  const candidate: WorkWindow | null = from && to ? { dayOfWeek: day!, startTime: from, endTime: to } : null
  const canAdd = candidate && validSchedule([...windows, candidate])
  return <form aria-label="Configurar horario" onSubmit={event => { event.preventDefault(); if (reviewed && validSchedule(windows)) void write.schedule(employee, windows) }}>
    <h3>Horario operacional · Lima</h3><p>Guardar reemplaza todas las ventanas. Para Pharmacist/Cashier, el horario vacío deniega la operación; el inicio se incluye y el fin se excluye. Owner tiene la excepción de horario definida por el backend.</p>
    <fieldset disabled={write.locked || !employee.isActive}><legend>Ventanas de trabajo</legend>
      <label htmlFor={id}>Día de la semana</label><select id={id} value={day} onChange={event => setDay(weekday(event.target.value))}>{days.map(value => <option key={value} value={value}>{dayLabels[value]}</option>)}</select>
      <TextField label="Hora de inicio" value={start} placeholder="09:00" onChange={event => setStart(event.target.value)} hint="HH:mm o HH:mm:ss, opcionalmente hasta seis decimales de segundo." />
      <TextField label="Hora de fin" value={end} placeholder="18:00" onChange={event => setEnd(event.target.value)} />
      <p>No se permiten ventanas duplicadas, inicio igual o posterior al fin ni cruces de medianoche. No se redondea la precisión horaria.</p>
      <Button variant="secondary" disabled={!canAdd} onClick={() => { if (candidate && canAdd) { setWindows(current => [...current, candidate]); setStart(''); setEnd(''); setReviewed(false) } }}>Agregar ventana al horario</Button>
      {windows.length ? <ul>{windows.map((window, index) => <li key={`${window.dayOfWeek}/${window.startTime}/${window.endTime}`}>{windowLabel(window)} <Button variant="quiet" onClick={() => { setWindows(current => current.filter((_, position) => position !== index)); setReviewed(false) }}>Quitar ventana {windowLabel(window)}</Button></li>)}</ul> : <p>Horario preparado vacío: no es acceso permanente para trabajadores.</p>}
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé y autorizo reemplazar el horario completo{windows.length ? '' : ' por un conjunto vacío'}.</label>
      <Button type="submit" disabled={!reviewed || !validSchedule(windows) || Boolean(start.trim() || end.trim())} pending={write.busy}>Guardar horario</Button>
      {(start.trim() || end.trim()) && <p>Agrega la ventana en edición o vacía sus campos antes de guardar; no se descarta silenciosamente.</p>}
    </fieldset>
  </form>
}
export function EmployeeDeactivation({ employee, write }: { employee: Employee; write: EmployeeWrite }) {
  const [reviewed, setReviewed] = useState(false)
  if (employee.role === 'owner') return <section aria-label="Desactivación de Owner bloqueada"><h3>Desactivación Owner: BLOCKED</h3><p>La protección del último Owner está pendiente de despliegue y validación segura en LAB. Hasta entonces se mantiene bloqueada toda desactivación de Owner, incluida la propia; contar una página de empleados no demuestra que sea segura.</p><Button disabled>Desactivar Owner · bloqueado</Button></section>
  if (!employee.isActive) return <p>Esta membresía está inactiva. Se conserva su historial; no hay endpoint de reactivación de esta membresía.</p>
  return <form aria-label="Desactivar empleado" onSubmit={event => { event.preventDefault(); if (reviewed && canDeactivate(employee)) void write.deactivate(employee) }}><h3>Desactivación segura de trabajador</h3>
    <p>Empleado: {employee.displayName}. Desactivar impide nuevas operaciones autorizadas y conserva el historial. La aplicación del trabajador deberá volver a verificar su acceso; el backend lo valida en cada operación.</p>
    <label><input type="checkbox" disabled={write.locked} checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Confirmo que deseo desactivar a {employee.displayName}.</label>
    <Button type="submit" variant="secondary" disabled={write.locked || !reviewed} pending={write.busy}>Desactivar empleado una vez</Button>
  </form>
}
