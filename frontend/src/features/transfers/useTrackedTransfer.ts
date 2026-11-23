import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ApiError } from '../../shared/api/http'
import { isUuid } from '../access'

// The two real transfer flows share response-loss tracking, not stock/cash business rules.
export function useTrackedTransfer<T extends { id: string }>(prefix: 'goods' | 'cashTransfer', read: (id: string, signal: AbortSignal) => Promise<T>, settled: (value: T, action: string) => boolean, verified: (value: T) => void, superseded?: (value: T, action: string) => boolean,
  creation?: { storageKey: string; recover: (requestId: string, signal: AbortSignal) => Promise<T> }) {
  const [params, setParams] = useSearchParams(); const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])
  const idKey = prefix + 'Id'; const pendingKey = prefix + 'Pending'; const unknownKey = prefix + 'Unidentified'
  const requestKey = prefix + 'Request'
  const [storedRequest, setStoredRequest] = useState(() => {
    try { return creation ? sessionStorage.getItem(creation.storageKey) : null } catch { return null }
  })
  const requestId = creation ? storedRequest ?? params.get(requestKey) : null
  const keyedCreation = requestId !== null
  const id = params.get(idKey) ?? ''; const pending = keyedCreation ? 'create' : params.get(pendingKey); const unidentified = params.has(unknownKey) && !keyedCreation
  const [initialId] = useState(keyedCreation ? '' : id); const [record, setRecord] = useState<T | null>(null)
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null); const initial = useRef<AbortController | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  function mark(action: string | null, target: string, unknown = latest.current.has(unknownKey), request?: string) {
    const next = new URLSearchParams(latest.current)
    if (action) next.set(pendingKey, action); else next.delete(pendingKey)
    if (target) next.set(idKey, target); else next.delete(idKey)
    if (action && unknown) next.set(unknownKey, '1'); else next.delete(unknownKey)
    if (creation && request) next.set(requestKey, request)
    else if (creation && action !== 'create') next.delete(requestKey)
    latest.current = next; setParams(next, { replace: true })
  }
  function releaseRequest(request: string) {
    if (!creation || !request) return true
    try {
      if (sessionStorage.getItem(creation.storageKey) === request) sessionStorage.removeItem(creation.storageKey)
    } catch { setError(new ApiError(0, 'client.transfer_recovery_storage_unavailable')); return false }
    setStoredRequest(null)
    return true
  }
  function apply(value: T) {
    setRecord(value); verified(value)
    const action = latest.current.get(pendingKey)
    const completed = action && settled(value, action)
    if (action && !latest.current.has(unknownKey) && (completed || superseded?.(value, action)) && releaseRequest(latest.current.get(requestKey) ?? '')) {
      mark(null, value.id); setError(null)
      setNotice(completed ? 'Estado verificado al consultar la misma transferencia. No se repitió la operación.' : 'Otra transición impide la operación pendiente. Se muestra el estado persistido; no se declara exitosa la acción solicitada ni se repite.')
    }
  }
  const observe = useEffectEvent((value: T) => apply(value))
  // Read/verified are stable callbacks of the selected workspace, whose key remounts this hook.
  useEffect(() => {
    if (!isUuid(initialId)) return
    const abort = new AbortController(); initial.current = abort; let active = true
    void Promise.resolve().then(() => read(initialId, abort.signal)).then(value => { if (active && !abort.signal.aborted) observe(value) }).catch(failure => { if (active && !abort.signal.aborted) setError(failure) })
    return () => { active = false; abort.abort() }
  }, [initialId, read])
  async function run(action: string, send: (signal: AbortSignal, requestId: string) => Promise<T>) {
    if (running.current || pending || latest.current.has(pendingKey) || denied) return false
    let request = ''
    if (creation && action === 'create') {
      try {
        const existing = sessionStorage.getItem(creation.storageKey)
        if (existing !== null) { setStoredRequest(existing); mark('create', '', false, existing); return false }
        request = crypto.randomUUID(); sessionStorage.setItem(creation.storageKey, request); setStoredRequest(request)
      } catch { setError(new ApiError(0, 'client.transfer_recovery_storage_unavailable')); return false }
    }
    initial.current?.abort(); running.current = true; setBusy(true); setError(null); setNotice('')
    let target = action === 'create' ? '' : record?.id ?? id; let acknowledged = false
    if (action === 'create') setRecord(null)
    mark(action, target, action === 'create' && !request, request); controller.current = new AbortController()
    try {
      const result = await send(controller.current.signal, request); acknowledged = true
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
        if (releaseRequest(request)) mark(null, target, false)
        if (failure.status === 401 || failure.status === 403) setDenied(true)
      }
      if (target && !request) { try { const current = await read(target, controller.current.signal); if (mounted.current && !controller.current.signal.aborted) apply(current) } catch { /* Preserve error and pending; no blind retry. */ } }
      return false
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult(target = id) {
    if (keyedCreation || !isUuid(target) || running.current || latest.current.has(pendingKey) && !latest.current.has(unknownKey) && id && target.toLowerCase() !== id.toLowerCase()) return
    initial.current?.abort(); running.current = true; setBusy(true); setError(null); setRecord(null); controller.current = new AbortController()
    mark(pending, target)
    try { const value = await read(target, controller.current.signal); if (mounted.current && !controller.current.signal.aborted) apply(value) }
    catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function recoverCreation() {
    if (!creation || !requestId || !isUuid(requestId) || running.current) return
    initial.current?.abort(); running.current = true; setBusy(true); setError(null); setRecord(null); controller.current = new AbortController()
    try {
      const signal = controller.current.signal
      const original = await creation.recover(requestId, signal)
      if (!mounted.current || signal.aborted) return
      mark('create', original.id, false, requestId)
      const current = await read(original.id, signal)
      if (mounted.current && !signal.aborted) apply(current)
    } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() { if (!record || running.current || !unidentified || !pending) return; mark(null, record.id, false); setError(null); setNotice('El operador concilió la creación original y su finalización. No se reenvió el POST.') }
  function startNew() { if (running.current || pending || denied) return; initial.current?.abort(); setRecord(null); setError(null); setNotice(''); mark(null, '', false) }
  return { id, pending, unidentified, keyedCreation, recoverCreation, record, busy, error, notice, run, consult, acknowledge, startNew, locked: busy || Boolean(pending) || denied }
}
