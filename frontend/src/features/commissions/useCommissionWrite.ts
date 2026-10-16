import { useEffect, useMemo, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { commissionsApi } from './api'
import type { Configuration, Rule, RuleInput } from './api'

type Action = 'settings' | 'rule' | 'deactivate'
export function useCommissionWrite() {
  const w = useWorkspace(); const { http } = useRuntime(); const client = useQueryClient(); const api = useMemo(() => http ? commissionsApi(http) : null, [http])
  const [params, setParams] = useSearchParams(); const latest = useRef(params); useEffect(() => { latest.current = params }, [params])
  const [busy, setBusy] = useState(false); const running = useRef(false); const [denied, setDenied] = useState(false); const abort = useRef<AbortController | null>(null); const mounted = useRef(true)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState(''); const [observed, setObserved] = useState<{ settings?: Configuration; rules?: Rule[] } | null>(null)
  const pending = params.get('commissionPending'); const productId = params.get('commissionProduct') ?? ''; const ruleId = params.get('commissionRule') ?? ''
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; abort.current?.abort() } }, [])
  function mark(action: Action | null, product = '', rule = '', ack = false) {
    const next = new URLSearchParams(latest.current); for (const key of ['commissionPending', 'commissionProduct', 'commissionRule', 'commissionAck']) next.delete(key)
    if (action) { next.set('commissionPending', action); if (product) next.set('commissionProduct', product); if (rule) next.set('commissionRule', rule); if (ack) next.set('commissionAck', '1') }
    latest.current = next; setParams(next, { replace: true })
  }
  const invalidate = () => { void client.invalidateQueries({ predicate: query => query.queryKey[0] === 'tenant' && query.queryKey[1] === w.tenantId && ['commissions-settings', 'commission-rules', 'owner-report'].includes(String(query.queryKey[3])) }) }
  async function read(signal: AbortSignal, offset = 0) {
    if (!api) throw new ApiError(0, 'client.https_required')
    const p = latest.current; const action = p.get('commissionPending'); const product = p.get('commissionProduct') ?? ''; const id = p.get('commissionRule') ?? ''
    const value = action === 'settings' ? { settings: await api.settings(w, signal) } : isUuid(product) ? { rules: await api.rules(w, product, true, offset, 100, signal) } : null
    if (!mounted.current || signal.aborted || !value) return
    setObserved(value); invalidate()
    const original = value.rules?.find(row => row.id.toLowerCase() === id.toLowerCase())
    if (action === 'deactivate' && original && !original.isActive || p.has('commissionAck') && (action === 'settings' && value.settings || action === 'rule' && original)) {
      mark(null); setError(null); setNotice('Operación reconocida y estado actual reconsultado. Una regla original puede estar inactiva por un cambio posterior; no se sustituyó por otra regla coincidente.')
    }
  }
  async function run(action: Action, input: boolean | { productId: string; rule: RuleInput } | Rule) {
    if (!api || running.current || latest.current.has('commissionPending') || denied) return
    const product = typeof input === 'boolean' ? '' : 'productId' in input ? input.productId : input.businessProductId
    const id = typeof input !== 'boolean' && 'id' in input ? input.id : ''
    running.current = true; setBusy(true); setError(null); setNotice(''); setObserved(null); mark(action, product, id); const controller = new AbortController(); abort.current = controller
    let acknowledged = false
    try {
      const result = action === 'settings' && typeof input === 'boolean' ? await api.enable(w, input, controller.signal) : action === 'rule' && typeof input !== 'boolean' && 'rule' in input ? await api.setRule(w, input.productId, input.rule, controller.signal) : action === 'deactivate' && typeof input !== 'boolean' && 'id' in input ? await api.deactivate(w, input, controller.signal) : null
      if (!mounted.current || controller.signal.aborted || !result) return
      acknowledged = true; mark(action, product, 'id' in result ? result.id : '', true); await read(controller.signal)
    } catch (failure) {
      if (!mounted.current) return
      setError(failure)
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) setDenied(true)
      if (!acknowledged && failure instanceof ApiError && !failure.outcomeUnknown) mark(null)
      if (!controller.signal.aborted && latest.current.has('commissionPending')) { try { await read(controller.signal) } catch { /* Keep pending; no blind reissue. */ } }
      invalidate()
    } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  async function consult(offset = 0) {
    if (running.current || !latest.current.has('commissionPending')) return
    running.current = true; setBusy(true); setError(null); setObserved(null); const controller = new AbortController(); abort.current = controller
    try { await read(controller.signal, offset) } catch (failure) { if (mounted.current && !controller.signal.aborted) { setError(failure); if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) setDenied(true) } } finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  function acknowledge() { if (!observed || running.current || latest.current.get('commissionPending') === 'deactivate') return; mark(null); setError(null); setNotice('El operador concilió inequívocamente la operación original y su finalización. No se repitió la escritura.'); invalidate() }
  return { pending, productId, ruleId, busy, error, notice, observed, locked: busy || Boolean(pending) || denied, run, consult, acknowledge }
}
