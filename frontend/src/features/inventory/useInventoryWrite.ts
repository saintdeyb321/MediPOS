import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { useRuntime, useWorkspace } from '../access'
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
  function mark(kind: 'adjustment' | 'minimum' | null, target = '') { setParams(current => { const next = new URLSearchParams(current); if (kind) { next.set('inventoryPending', kind); next.set('inventoryTarget', target) } else { next.delete('inventoryPending'); next.delete('inventoryTarget') } return next }, { replace: true }) }
  async function run(kind: 'adjustment' | 'minimum', target: string, write: (signal: AbortSignal) => Promise<Adjustment | StockThreshold>) {
    if (!http || running.current || pending || denied || !['owner', 'pharmacist'].includes(w.access.role) || kind === 'minimum' && w.access.role !== 'owner') return
    running.current = true; setBusy(true); setError(null); setResult(null); mark(kind, target); controller.current = new AbortController()
    try {
      const saved = await write(controller.current.signal)
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
  return { busy, pending, error, result, locked: busy || pending || denied || !http,
    adjust: (lotId: string, quantity: JsonNumber, reason: string) => run('adjustment', lotId, signal => inventoryApi(http!).adjust(w, lotId, quantity, reason, signal)),
    minimum: (productId: string, quantity: JsonNumber) => run('minimum', productId, signal => inventoryApi(http!).minimum(w, productId, quantity, signal)),
  }
}
export type InventoryWrite = ReturnType<typeof useInventoryWrite>
