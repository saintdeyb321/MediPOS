import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import type { JsonNumber } from '../../shared/api/exactJson'
import { inventoryApi } from './api'
import type { Adjustment, StockThreshold } from './api'

export function useInventoryWrite() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [params, setParams] = useSearchParams(); const pending = params.has('inventoryPending')
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [result, setResult] = useState<Adjustment | StockThreshold | null>(null)
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  function mark(kind: 'adjustment' | 'minimum' | null, target = '', requestId = '') { setParams(current => { const next = new URLSearchParams(current); if (kind) { next.set('inventoryPending', kind); next.set('inventoryTarget', target); next.set('inventoryRequest', requestId) } else { next.delete('inventoryPending'); next.delete('inventoryTarget'); next.delete('inventoryRequest') } return next }, { replace: true }) }
  async function run(kind: 'adjustment' | 'minimum', target: string, write: (signal: AbortSignal, requestId: string) => Promise<Adjustment | StockThreshold>) {
    if (!http || running.current || pending || denied || !['owner', 'pharmacist'].includes(w.access.role) || kind === 'minimum' && w.access.role !== 'owner') return
    running.current = true; setBusy(true); setError(null); setResult(null); const requestId = crypto.randomUUID(); mark(kind, target, requestId); controller.current = new AbortController()
    try {
      const saved = await write(controller.current.signal, requestId)
      if (!mounted.current) return
      setResult(saved); mark(null)
      for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) void client.invalidateQueries({ queryKey: scopedKey(w, resource) })
      return true
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && !failure.outcomeUnknown) { mark(null); if (failure.status === 401 || failure.status === 403) setDenied(true) }
    } finally { running.current = false; if (mounted.current) setBusy(false) }
    return false
  }
  async function reconcile() {
    const requestId = params.get('inventoryRequest'); const kind = params.get('inventoryPending')
    if (!http || running.current || !requestId || (kind !== 'adjustment' && kind !== 'minimum')) return
    running.current = true; setBusy(true); setError(null); const abort = new AbortController(); controller.current = abort
    try {
      const saved = await inventoryApi(http).recover(w, requestId, kind, abort.signal)
      const target = params.get('inventoryTarget')
      if (('stockMovementId' in saved ? saved.inventoryLotId : saved.businessProductId) !== target) throw new ApiError(0, 'client.unexpected_response')
      if (mounted.current) { setResult(saved); mark(null); for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) void client.invalidateQueries({ queryKey: scopedKey(w, resource) }) }
    } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  useNavigationProtection({ busy, pending })
  return { reconcile, recoverable: isUuid(params.get('inventoryRequest') ?? ''), busy, pending, error, result, locked: busy || pending || denied || !http,
    adjust: (lotId: string, quantity: JsonNumber, reason: string) => run('adjustment', lotId, (signal, requestId) => inventoryApi(http!).adjust(w, lotId, quantity, reason, signal, { id: requestId, branchId: w.branchId })),
    minimum: (productId: string, quantity: JsonNumber) => run('minimum', productId, (signal, requestId) => inventoryApi(http!).minimum(w, productId, quantity, signal, { id: requestId, branchId: w.branchId })),
  }
}
export type InventoryWrite = ReturnType<typeof useInventoryWrite>
