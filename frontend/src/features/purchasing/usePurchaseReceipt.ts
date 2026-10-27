import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { purchasingApi } from './api'
import type { PurchaseLineInput, PurchaseView } from './api'

type WriteKind = 'create' | 'lines' | 'confirm'
export function usePurchaseReceipt() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [params, setParams] = useSearchParams(); const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])
  const id = params.get('purchase') ?? ''; const pending = params.get('receiptPending')
  const [server, setServer] = useState<PurchaseView | null>(null)
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const [expectedId, setExpectedId] = useState(id)
  const [initialId] = useState(id)
  const { tenantId, branchId } = w
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null)
  const initialRead = useRef<AbortController | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  function mark(kind: WriteKind | null, purchaseId = latest.current.get('purchase') ?? '') {
    const next = new URLSearchParams(latest.current)
    if (kind) next.set('receiptPending', kind); else next.delete('receiptPending')
    if (purchaseId) next.set('purchase', purchaseId); else next.delete('purchase')
    latest.current = next; setParams(next, { replace: true })
  }
  function invalidateStock() {
    for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) void client.invalidateQueries({ queryKey: scopedKey(w, resource) })
  }
  async function read(purchaseId: string, signal: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const result = await purchasingApi(http).read(w, purchaseId, signal)
    if (!mounted.current || signal.aborted) return
    setServer(result)
    if (result.purchase.status === 'confirmed') {
      // A confirmed search hit cannot prove which creation committed when its response had no ID.
      if (latest.current.get('receiptPending') === 'confirm') mark(null, purchaseId)
      invalidateStock()
    }
    return result
  }
  // Restore only the addressed receipt. Changing scope remounts the controller and aborts its requests.
  useEffect(() => {
    if (!http || !isUuid(initialId)) return
    const readController = new AbortController(); initialRead.current = readController; let active = true
    void purchasingApi(http).read({ tenantId, branchId }, initialId, readController.signal).then(result => {
      if (active && !readController.signal.aborted) setServer(result)
    }).catch(failure => { if (active && !readController.signal.aborted) setError(failure) })
    return () => { active = false; readController.abort() }
  }, [initialId, http, tenantId, branchId])
  async function run(kind: WriteKind, write: (signal: AbortSignal) => Promise<string>) {
    if (!http || running.current || latest.current.has('receiptPending') || denied || !['owner', 'pharmacist'].includes(w.access.role)) return
    initialRead.current?.abort()
    running.current = true; setBusy(true); setError(null); setNotice(''); mark(kind)
    controller.current = new AbortController(); let acknowledged = false; let target = id
    if (kind === 'create') { target = ''; setExpectedId(''); setServer(null) }
    try {
      target = await write(controller.current.signal); acknowledged = true
      if (!mounted.current) return
      setExpectedId(target); mark(kind, target)
      const result = await read(target, controller.current.signal)
      if (!mounted.current) return
      if (result && (kind !== 'confirm' || result.purchase.status === 'confirmed')) {
        mark(null, target); setNotice(kind === 'confirm' ? 'MERCADERÍA REGISTRADA · recepción confirmada y consultada en el servidor.' : 'Recepción guardada y consultada en el servidor. Aún no ingresa mercadería.')
        return true
      }
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (!acknowledged && failure instanceof ApiError && !failure.outcomeUnknown) {
        mark(null, target)
        if (failure.status === 401 || failure.status === 403) setDenied(true)
      }
      if (target) { try { await read(target, controller.current.signal) } catch { /* Original error and pending marker remain. */ } }
    } finally { running.current = false; if (mounted.current) setBusy(false) }
    return false
  }
  async function consult(purchaseId = id) {
    if (!isUuid(purchaseId) || !http || running.current || expectedId && pending && expectedId.toLowerCase() !== purchaseId.toLowerCase()) return
    initialRead.current?.abort()
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController()
    mark(pending as WriteKind | null, purchaseId)
    try { await read(purchaseId, controller.current.signal) } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() {
    if (running.current || !server || !pending || pending === 'confirm' || expectedId && expectedId.toLowerCase() !== server.purchase.id.toLowerCase()) return
    mark(null, server.purchase.id); setError(null); setNotice('El operador concilió el registro original y la finalización de la solicitud. No se repitió la escritura.')
  }
  function startNew() {
    if (running.current || pending || denied) return
    initialRead.current?.abort()
    setServer(null); setExpectedId(''); setError(null); setNotice(''); mark(null, '')
  }
  useNavigationProtection({ busy, pending: Boolean(pending) })
  return { id, pending, server, busy, error, notice, consult, acknowledge, startNew, locked: busy || Boolean(pending) || denied || !http,
    create: (supplierId: string | null, reference: string | null) => run('create', async signal => (await purchasingApi(http!).create(w, supplierId, reference, signal)).id),
    save: (lines: PurchaseLineInput[]) => { if (server?.purchase.status === 'draft') return run('lines', async signal => { await purchasingApi(http!).lines(w, server.purchase.id, lines, server.revision, signal); return server.purchase.id }) },
    confirm: () => { if (server?.purchase.status === 'draft' && server.lines.length) return run('confirm', async signal => {
      return (await purchasingApi(http!).confirm(w, server.purchase.id, server.revision, signal)).id
    }) },
  }
}
export type ReceiptController = ReturnType<typeof usePurchaseReceipt>
