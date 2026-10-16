import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError } from '../../shared/api/http'
import { isUuid } from '../access'

// The two real transfer flows share response-loss tracking, not stock/cash business rules.
export function useTrackedTransfer<T extends { id: string }>(prefix: 'goods' | 'cashTransfer', read: (id: string, signal: AbortSignal) => Promise<T>, settled: (value: T, action: string) => boolean, verified: (value: T) => void, superseded?: (value: T, action: string) => boolean) {
  const [params, setParams] = useSearchParams(); const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])
  const idKey = prefix + 'Id'; const pendingKey = prefix + 'Pending'; const unknownKey = prefix + 'Unidentified'
  const id = params.get(idKey) ?? ''; const pending = params.get(pendingKey); const unidentified = params.has(unknownKey)
  const [initialId] = useState(id); const [record, setRecord] = useState<T | null>(null)
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null); const initial = useRef<AbortController | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  function mark(action: string | null, target: string, unknown = latest.current.has(unknownKey)) {
    const next = new URLSearchParams(latest.current)
    if (action) next.set(pendingKey, action); else next.delete(pendingKey)
    if (target) next.set(idKey, target); else next.delete(idKey)
    if (action && unknown) next.set(unknownKey, '1'); else next.delete(unknownKey)
    latest.current = next; setParams(next, { replace: true })
  }
  function apply(value: T) {
    setRecord(value); verified(value)
    const action = latest.current.get(pendingKey)
    if (action && !latest.current.has(unknownKey) && (settled(value, action) || superseded?.(value, action))) { mark(null, value.id); setError(null); setNotice(settled(value, action) ? 'Estado verificado al consultar la misma transferencia. No se repitió la operación.' : 'Otra transición impide la operación pendiente. Se muestra el estado persistido; no se declara exitosa la acción solicitada ni se repite.') }
  }
  const observe = useEffectEvent((value: T) => apply(value))
  // Read/verified are stable callbacks of the selected workspace, whose key remounts this hook.
  useEffect(() => {
    if (!isUuid(initialId)) return
    const abort = new AbortController(); initial.current = abort; let active = true
    void Promise.resolve().then(() => read(initialId, abort.signal)).then(value => { if (active && !abort.signal.aborted) observe(value) }).catch(failure => { if (active && !abort.signal.aborted) setError(failure) })
    return () => { active = false; abort.abort() }
  }, [initialId, read])
  async function run(action: string, send: (signal: AbortSignal) => Promise<T>) {
    if (running.current || latest.current.has(pendingKey) || denied) return false
    initial.current?.abort(); running.current = true; setBusy(true); setError(null); setNotice('')
    let target = action === 'create' ? '' : record?.id ?? id; let acknowledged = false
    if (action === 'create') setRecord(null)
    mark(action, target, action === 'create'); controller.current = new AbortController()
    try {
      const result = await send(controller.current.signal); acknowledged = true
      if (!mounted.current) return false
      target = result.id; mark(action, target, false)
      const current = await read(target, controller.current.signal)
      if (!mounted.current || controller.current.signal.aborted) return false
      apply(current)
      return settled(current, action)
    } catch (failure) {
      if (!mounted.current) return false
      setError(failure)
      if (!acknowledged && failure instanceof ApiError && !failure.outcomeUnknown) {
        mark(null, target, false)
        if (failure.status === 401 || failure.status === 403) setDenied(true)
      }
      if (target) { try { const current = await read(target, controller.current.signal); if (mounted.current && !controller.current.signal.aborted) apply(current) } catch { /* Preserve error and pending; no blind retry. */ } }
      return false
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult(target = id) {
    if (!isUuid(target) || running.current || latest.current.has(pendingKey) && !latest.current.has(unknownKey) && id && target.toLowerCase() !== id.toLowerCase()) return
    initial.current?.abort(); running.current = true; setBusy(true); setError(null); setRecord(null); controller.current = new AbortController()
    mark(pending, target)
    try { const value = await read(target, controller.current.signal); if (mounted.current && !controller.current.signal.aborted) apply(value) }
    catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() { if (!record || running.current || !unidentified || !pending) return; mark(null, record.id, false); setError(null); setNotice('El operador concilió la creación original y su finalización. No se reenvió el POST.') }
  function startNew() { if (running.current || pending || denied) return; initial.current?.abort(); setRecord(null); setError(null); setNotice(''); mark(null, '', false) }
  return { id, pending, unidentified, record, busy, error, notice, run, consult, acknowledge, startNew, locked: busy || Boolean(pending) || denied }
}
