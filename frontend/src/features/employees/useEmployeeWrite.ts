import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { employeesApi, canDeactivate, sameId } from './api'
import type { Employee } from './api'
import type { WorkWindow } from './schedule'
import { resetOwnWorkspace } from './accessReset'

type Kind = 'branches' | 'schedule' | 'deactivate'
export function useEmployeeWrite(offset: number) {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [params, setParams] = useSearchParams(); const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])
  const pending = params.get('employeePending'); const targetId = params.get('employee') ?? ''
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState(''); const [observed, setObserved] = useState<Employee | null>(null)
  const controller = useRef<AbortController | null>(null); const mounted = useRef(true); const selfMutation = useRef(false)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false; controller.current?.abort()
      // A cancelled response does not prove a self-access write failed on the server.
      if (selfMutation.current) { selfMutation.current = false; resetOwnWorkspace(client, w.tenantId) }
    }
  }, [client, w.tenantId])
  function mark(kind: Kind | null, employeeId = targetId) {
    const next = new URLSearchParams(latest.current)
    if (kind) next.set('employeePending', kind); else next.delete('employeePending')
    next.set('employee', employeeId); next.set('employeePage', String(offset))
    latest.current = next; setParams(next, { replace: true })
  }
  async function read(employeeId: string, signal: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const rows = await employeesApi(http).list(w, offset, 20, signal)
    if (!mounted.current || signal.aborted) return
    client.setQueryData(scopedKey(w, 'employees', w.session.userId, w.access.membershipId, offset), rows)
    const result = rows.find(row => sameId(row.membershipId, employeeId))
    if (!result) throw new ApiError(404, 'employees.not_on_page')
    setObserved(result)
    if (latest.current.get('employeePending') === 'deactivate' && !result.isActive) {
      mark(null, employeeId); setError(null); setNotice('Desactivación verificada al consultar la misma membresía inactiva. No se repitió el POST.')
    }
    return result
  }
  async function run(kind: Kind, employee: Employee, write: (signal: AbortSignal) => Promise<unknown>, matches: (current: Employee) => boolean) {
    if (!http || running.current || latest.current.has('employeePending') || denied || w.access.role !== 'owner' || !employee.isActive || kind === 'deactivate' && !canDeactivate(employee)) return
    running.current = true; setBusy(true); setError(null); setNotice(''); setObserved(null); mark(kind, employee.membershipId); controller.current = new AbortController()
    let acknowledged = false; let reevaluate = sameId(employee.userId, w.session.userId) || sameId(employee.membershipId, w.access.membershipId)
    selfMutation.current = reevaluate
    try {
      await client.cancelQueries({ queryKey: scopedKey(w, 'employees', w.session.userId, w.access.membershipId) })
      await write(controller.current.signal); acknowledged = true
      if (!mounted.current) return
      const current = await read(employee.membershipId, controller.current.signal)
      if (!mounted.current) return
      if (current && matches(current)) {
        mark(null, employee.membershipId); setNotice('Cambio guardado y empleado consultado nuevamente en el servidor. No se repitió la operación.')
        void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && query.queryKey[3] === 'employees', refetchType: 'none' })
        return true
      }
      setNotice('El servidor respondió, pero la lectura no coincide con lo enviado. Compara el estado actual antes de otra escritura.')
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (!acknowledged && failure instanceof ApiError && !failure.outcomeUnknown) {
        mark(null, employee.membershipId)
        if (failure.status === 401 || failure.status === 403) { setDenied(true); reevaluate = true }
      }
      try { await read(employee.membershipId, controller.current.signal) } catch { /* Keep failure and pending status; no fabricated rollback. */ }
    } finally {
      running.current = false
      if (reevaluate && (mounted.current || selfMutation.current)) { selfMutation.current = false; resetOwnWorkspace(client, w.tenantId) }
      if (mounted.current) setBusy(false)
    }
    return false
  }
  async function consult() {
    if (!targetId || !http || running.current) return
    running.current = true; setBusy(true); setError(null); setObserved(null); controller.current = new AbortController()
    try { await read(targetId, controller.current.signal) } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() {
    if (running.current || !observed || !pending || pending === 'deactivate' || !sameId(observed.membershipId, targetId)) return
    mark(null); setError(null); setNotice('El operador verificó el registro original y concilió la finalización. La escritura no se volvió a enviar.')
    if (sameId(observed.userId, w.session.userId)) resetOwnWorkspace(client, w.tenantId)
  }
  const idsKey = (ids: readonly string[]) => [...ids].map(id => id.toLowerCase()).sort().join('/')
  const scheduleKey = (windows: readonly WorkWindow[]) => windows.map(row => `${row.dayOfWeek}/${row.startTime}/${row.endTime}`).sort().join('|')
  return { busy, pending, targetId, error, notice, observed, consult, acknowledge, locked: busy || Boolean(pending) || denied || !http,
    branches: (employee: Employee, ids: string[], available: readonly string[]) => run('branches', employee, signal => employeesApi(http!).branches(w, employee, ids, available, signal), row => idsKey(row.branchIds) === idsKey(ids)),
    schedule: (employee: Employee, windows: WorkWindow[]) => run('schedule', employee, signal => employeesApi(http!).schedule(w, employee, windows, signal), row => scheduleKey(row.schedule) === scheduleKey(windows)),
    deactivate: (employee: Employee) => run('deactivate', employee, signal => employeesApi(http!).deactivate(w, employee, signal), row => !row.isActive),
  }
}
export type EmployeeWrite = ReturnType<typeof useEmployeeWrite>
