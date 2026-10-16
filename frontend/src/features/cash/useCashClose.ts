import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { decimalInput, maximumMoney } from '../../shared/api/decimal'
import { cashApi } from './api'
import type { CashReconciliation, CashSessionStatus } from './api'

export function useCashClose() {
  const { http } = useRuntime(); const { tenantId, branchId } = useWorkspace()
  const client = useQueryClient(); const [parameters, setParameters] = useSearchParams()
  const latest = useRef(parameters); useEffect(() => { latest.current = parameters }, [parameters])
  const [status, setStatus] = useState<CashSessionStatus | null>(null)
  const [receipt, setReceipt] = useState<CashReconciliation | null>(null)
  const [error, setError] = useState<unknown>(null); const [busy, setBusy] = useState(false)
  const [denied, setDenied] = useState(false)
  const running = useRef(false); const controller = useRef<AbortController | null>(null); const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  const pending = parameters.has('closingCash')
  const id = parameters.get('cash') ?? ''
  function mark(id: string, uncertain: boolean) {
    const next = new URLSearchParams(latest.current); next.set('cash', id)
    if (uncertain) next.set('closingCash', '1'); else next.delete('closingCash')
    latest.current = next; setParameters(next, { replace: true })
  }
  async function read(selected: string, signal: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const api = cashApi(http); const current = await api.status({ tenantId, branchId }, selected, signal)
    if (!mounted.current) return
    setStatus(current)
    if (current.status !== 'closed') return false
    // A closed status is sufficient to prevent another close; receipt failure is a separate read error.
    mark(selected, false)
    void client.invalidateQueries({ queryKey: scopedKey({ tenantId, branchId }, 'current-cash') })
    void client.invalidateQueries({ queryKey: scopedKey({ tenantId, branchId }, 'active-cash') })
    const result = await api.reconciliation({ tenantId, branchId }, selected, signal)
    if (mounted.current) { setReceipt(result); setError(null) }
    return true
  }
  async function consult(selected = id) {
    if (!isUuid(selected) || running.current) return
    running.current = true; setBusy(true); setError(null); setReceipt(null); setStatus(null)
    controller.current = new AbortController(); mark(selected, pending)
    try { await read(selected, controller.current.signal) }
    catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function close(selected: string, amount: string) {
    const counted = decimalInput(amount, maximumMoney, 4, true)
    if (!http || !counted || !isUuid(selected) || pending || running.current || denied || status?.cashSessionId !== selected || status.status !== 'open') return
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController(); mark(selected, true)
    try {
      await cashApi(http).close({ tenantId, branchId }, selected, counted, controller.current.signal)
      if (mounted.current) await read(selected, controller.current.signal)
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) setDenied(true)
      // Only a definitive rejection permits a new explicit attempt. An early open read cannot prove rollback.
      if (failure instanceof ApiError && !failure.outcomeUnknown) mark(selected, false)
      try { await read(selected, controller.current.signal) } catch { /* Preserve the original error and unresolved marker. */ }
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  return { id, pending, status, receipt, busy, error, denied, consult, close }
}
