import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
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
  async function readReconciliation(selected: string, signal: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const result = await cashApi(http).reconciliation({ tenantId, branchId }, selected, signal)
    if (mounted.current && !signal.aborted) { setReceipt(result); setError(null) }
  }
  async function readStatus(selected: string, signal: AbortSignal) {
    if (!http) throw new ApiError(0, 'client.https_required')
    const current = await cashApi(http).status({ tenantId, branchId }, selected, signal)
    if (!mounted.current || signal.aborted) return
    setStatus(current)
    if (current.status !== 'closed') return
    // A closed status is sufficient to prevent another close; receipt failure is a separate read error.
    mark(selected, false)
    void client.invalidateQueries({ queryKey: scopedKey({ tenantId, branchId }, 'current-cash') })
    void client.invalidateQueries({ queryKey: scopedKey({ tenantId, branchId }, 'active-cash') })
    await readReconciliation(selected, signal)
  }
  async function consult(selected = id) {
    const unresolved = latest.current.has('closingCash')
    if (!isUuid(selected) || running.current || (unresolved && latest.current.get('cash') !== selected)) return
    const alreadyClosed = !unresolved && status?.cashSessionId === selected && status.status === 'closed'
    running.current = true; setBusy(true); setError(null); setReceipt(null)
    if (!alreadyClosed) setStatus(null)
    controller.current = new AbortController(); mark(selected, unresolved)
    try {
      if (alreadyClosed) await readReconciliation(selected, controller.current.signal)
      else await readStatus(selected, controller.current.signal)
    }
    catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function close(selected: string, amount: string) {
    const counted = decimalInput(amount, maximumMoney, 4, true)
    if (!http || !counted || !isUuid(selected) || latest.current.has('closingCash') || running.current || denied ||
      latest.current.get('cash') !== selected || status?.cashSessionId !== selected || status.status !== 'open') return
    running.current = true; setBusy(true); setError(null); setStatus(null); setReceipt(null)
    controller.current = new AbortController(); mark(selected, true)
    const signal = controller.current.signal
    try {
      try {
        await cashApi(http).close({ tenantId, branchId }, selected, counted, signal)
      } catch (failure) {
        if (!mounted.current || signal.aborted) return
        setError(failure)
        if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) setDenied(true)
        // Only the POST can definitively reject the write.
        if (failure instanceof ApiError && !failure.outcomeUnknown) mark(selected, false)
      }
      if (!mounted.current || signal.aborted) return
      try { await readStatus(selected, signal) }
      catch (failure) {
        if (mounted.current && !signal.aborted) setError((previous: unknown) => previous ?? failure)
      }
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  useNavigationProtection({ busy, pending })
  return { id, pending, status, receipt, busy, error, denied, consult, close }
}
