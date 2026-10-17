import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { catalogApi } from './api'
import type { ProductDetail } from './api'

type Kind = 'create' | 'global' | 'prices' | 'status' | 'units'
export function useCatalogWrite(select: (id: string) => void) {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [parameters, setParameters] = useSearchParams(); const latest = useRef(parameters)
  const [expected, setExpected] = useState<{ id?: string; code?: string }>(() => ({ id: parameters.get('catalogProduct') ?? undefined }))
  useEffect(() => { latest.current = parameters }, [parameters])
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const [observed, setObserved] = useState<ProductDetail | null>(null)
  const controller = useRef<AbortController | null>(null); const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  const pending = parameters.has('catalogPending')
  function mark(kind: Kind | null, id?: string) {
    const next = new URLSearchParams(latest.current)
    if (kind) next.set('catalogPending', kind); else { next.delete('catalogPending'); next.delete('catalogProduct') }
    if (id) next.set('catalogProduct', id)
    latest.current = next; setParameters(next, { replace: true })
  }
  async function refresh(id: string, signal?: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const result = await catalogApi(http).detail(w, id, signal)
    if (!mounted.current) return
    setObserved(result); select(id)
    client.setQueryData(scopedKey(w, 'catalog-detail', w.session.userId, w.access.membershipId, id), result)
    if (latest.current.get('catalogPending') === 'units') {
      const configuration = await catalogApi(http).unitConfiguration(w, id, signal)
      if (!mounted.current) return
      client.setQueryData(scopedKey(w, 'unit-configuration', w.session.userId, w.access.membershipId, id), configuration)
    }
    for (const resource of ['catalog-products', 'product-search']) void client.invalidateQueries({ queryKey: scopedKey(w, resource) })
    return result
  }
  async function run(kind: Kind, id: string | undefined, write: (signal: AbortSignal) => Promise<string>, code?: string) {
    if (running.current || pending || denied || !http || w.access.role !== 'owner') return
    running.current = true; setBusy(true); setError(null); setNotice(''); setObserved(null)
    controller.current = new AbortController(); mark(kind, id)
    setExpected({ id, code })
    let acknowledged = false; let affected = id
    try {
      affected = await write(controller.current.signal); acknowledged = true
      if (!mounted.current) return
      setExpected(current => ({ ...current, id: affected }))
      mark(kind, affected)
      await refresh(affected, controller.current.signal)
      if (!mounted.current) return
      mark(null); setNotice('Operación guardada y producto consultado nuevamente en el servidor. No se crearon existencias.')
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (!acknowledged && failure instanceof ApiError && !failure.outcomeUnknown) {
        mark(null)
        if (failure.status === 401 || failure.status === 403) setDenied(true)
      }
      // Keep the marker on response loss. An early read alone cannot prove rollback or justify another write.
      if (affected) { try { await refresh(affected, controller.current.signal) } catch { /* Keep the original failure. */ } }
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult(id = parameters.get('catalogProduct') ?? '') {
    if (!isUuid(id) || running.current) return
    running.current = true; setBusy(true); controller.current = new AbortController()
    try { await refresh(id, controller.current.signal) } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  const canAcknowledge = Boolean(observed && (!expected.id || expected.id.toLowerCase() === observed.product.id.toLowerCase()) && (!expected.code || expected.code === observed.product.internalCode))
  function acknowledge(code: string) {
    if (!observed || running.current || !pending || !canAcknowledge || code.trim() !== observed.product.internalCode) return
    mark(null); setError(null); setNotice('El operador revisó el estado consultado y concilió la operación. No se repitió la escritura.')
  }
  return { busy, pending, denied, error, notice, observed, run, consult, acknowledge, canAcknowledge, locked: busy || pending || denied || !http }
}
export type CatalogWrite = ReturnType<typeof useCatalogWrite>
