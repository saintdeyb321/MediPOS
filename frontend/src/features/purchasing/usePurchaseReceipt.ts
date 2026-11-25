import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { purchasingApi } from './api'
import type { Purchase, PurchaseLineInput, PurchaseView } from './api'

type WriteKind = 'create' | 'lines' | 'confirm'
export function usePurchaseReceipt() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient()
  const [params, setParams] = useSearchParams(); const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])
  const { tenantId, branchId } = w
  const creationKey = `medipos-purchase-creation/${w.session.userId}/${w.access.membershipId}/${tenantId}/${branchId}`
  const [storedRequest, setStoredRequest] = useState(() => {
    try { return sessionStorage.getItem(creationKey) } catch { return null }
  })
  const id = params.get('purchase') ?? ''; const pending = storedRequest ? 'create' : params.get('receiptPending')
  const creationPending = pending === 'create' && (Boolean(storedRequest) || params.has('receiptRequest'))
  const [server, setServer] = useState<PurchaseView | null>(null)
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const [expectedId, setExpectedId] = useState(id)
  const [initialId] = useState(creationPending ? '' : id)
  const mounted = useRef(true); const controller = useRef<AbortController | null>(null)
  const initialRead = useRef<AbortController | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  function mark(kind: WriteKind | null, purchaseId = latest.current.get('purchase') ?? '', requestId?: string) {
    const next = new URLSearchParams(latest.current)
    if (kind) next.set('receiptPending', kind); else next.delete('receiptPending')
    if (purchaseId) next.set('purchase', purchaseId); else next.delete('purchase')
    if (requestId) next.set('receiptRequest', requestId)
    else if (kind !== 'create') next.delete('receiptRequest')
    latest.current = next; setParams(next, { replace: true })
  }
  function invalidateStock() {
    for (const resource of ['stock', 'inventory-lots', 'inventory-expiring', 'product-search']) void client.invalidateQueries({ queryKey: scopedKey(w, resource) })
  }
  function clearCreation(requestId: string, purchaseId: string) {
    if (sessionStorage.getItem(creationKey) === requestId) sessionStorage.removeItem(creationKey)
    setStoredRequest(null); mark(null, purchaseId)
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
  async function run(kind: Exclude<WriteKind, 'create'>, write: (signal: AbortSignal) => Promise<Purchase | void>) {
    if (!http || !server || running.current || latest.current.has('receiptPending') || denied || !['owner', 'pharmacist'].includes(w.access.role)) return
    initialRead.current?.abort()
    running.current = true; setBusy(true); setError(null); setNotice(''); mark(kind)
    controller.current = new AbortController(); let acknowledged = false
    const target = server.purchase.id
    try {
      const confirmed = await write(controller.current.signal); acknowledged = true
      if (!mounted.current) return
      if (confirmed) {
        // The POST validates this exact reviewed revision inside the confirmation transaction.
        // Confirmation keeps its reviewed lines and is terminal.
        setServer({ ...server, purchase: confirmed })
        mark(null, target); invalidateStock(); setNotice('Recepción confirmada por el servidor.')
        return true
      }
      setExpectedId(target); mark(kind, target)
      const result = await read(target, controller.current.signal)
      if (!mounted.current) return
      if (result) {
        mark(null, target); setNotice('Recepción guardada y consultada en el servidor. Aún no ingresa mercadería.')
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
  async function resolveCreation(requestId: string, purchase: Purchase, signal: AbortSignal) {
    if (!mounted.current || signal.aborted) return
    if (purchase.createdByActorId.toLowerCase() !== w.session.userId.toLowerCase()) throw new ApiError(0, 'client.unexpected_response')
    setExpectedId(purchase.id); mark('create', purchase.id, requestId)
    const result = await read(purchase.id, signal)
    if (!result) return
    clearCreation(requestId, purchase.id)
    setNotice('Recepción guardada y consultada en el servidor. El alta del borrador no registra mercadería.')
  }
  async function create(supplierId: string | null, reference: string | null) {
    if (!http || running.current || pending || latest.current.has('receiptPending') || denied || !['owner', 'pharmacist'].includes(w.access.role)) return
    const requestId = crypto.randomUUID()
    try {
      const previous = sessionStorage.getItem(creationKey)
      if (previous) { setStoredRequest(previous); mark('create', '', previous); return }
      sessionStorage.setItem(creationKey, requestId)
    } catch { setError(new ApiError(0, 'client.purchase_recovery_storage_unavailable')); return }
    setStoredRequest(requestId)
    initialRead.current?.abort()
    const abort = new AbortController(); controller.current = abort
    running.current = true; setBusy(true); setError(null); setNotice(''); setServer(null); setExpectedId(''); mark('create', '', requestId)
    try {
      let original: Purchase
      try {
        original = await purchasingApi(http).create(w, supplierId, reference, abort.signal, { id: requestId, branchId })
      } catch (failure) {
        if (mounted.current && !abort.signal.aborted && failure instanceof ApiError && !failure.outcomeUnknown) {
          if (failure.status === 401 || failure.status === 403) setDenied(true)
          clearCreation(requestId, '')
        }
        throw failure
      }
      await resolveCreation(requestId, original, abort.signal)
    } catch (failure) { if (mounted.current && !abort.signal.aborted) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function recoverCreation() {
    const requestId = storedRequest ?? latest.current.get('receiptRequest') ?? ''
    if (!http || running.current || !creationPending || !isUuid(requestId)) return
    const abort = new AbortController(); controller.current = abort
    running.current = true; setBusy(true); setError(null)
    try { await resolveCreation(requestId, await purchasingApi(http).recoverCreation(w, requestId, abort.signal), abort.signal) }
    catch (failure) { if (mounted.current && !abort.signal.aborted) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult(purchaseId = id) {
    if (creationPending || !isUuid(purchaseId) || !http || running.current || expectedId && pending && expectedId.toLowerCase() !== purchaseId.toLowerCase()) return
    initialRead.current?.abort()
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController()
    mark(pending as WriteKind | null, purchaseId)
    try { await read(purchaseId, controller.current.signal) } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() {
    if (creationPending || running.current || !server || !pending || pending === 'confirm' || expectedId && expectedId.toLowerCase() !== server.purchase.id.toLowerCase()) return
    mark(null, server.purchase.id); setError(null); setNotice('El operador concilió el registro original y la finalización de la solicitud. No se repitió la escritura.')
  }
  function startNew() {
    if (running.current || pending || denied) return
    initialRead.current?.abort()
    setServer(null); setExpectedId(''); setError(null); setNotice(''); mark(null, '')
  }
  useNavigationProtection({ busy, pending: Boolean(pending) })
  return { id, pending, creationPending, recoverCreation, server, busy, error, notice, consult, acknowledge, startNew, locked: busy || Boolean(pending) || denied || !http,
    create,
    save: (lines: PurchaseLineInput[]) => { if (server?.purchase.status === 'draft') return run('lines', async signal => { await purchasingApi(http!).lines(w, server.purchase.id, lines, server.revision, signal) }) },
    confirm: () => { if (server?.purchase.status === 'draft' && server.lines.length) return run('confirm',
      signal => purchasingApi(http!).confirm(w, server.purchase.id, server.revision, signal)) },
  }
}
export type ReceiptController = ReturnType<typeof usePurchaseReceipt>
