import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import type { OpenCashSession } from '../cash'
import { cartError, matchesSaved, restoreCart, saleInputs } from './cart'
import type { CartLine } from './cart'
import { salesApi } from './sales.api'
import type { SaleState } from './sales.api'

type Phase = 'idle' | 'loading' | 'creating' | 'saving' | 'uncertain' | 'conflict' | 'blocked'
export function useSaleDraft(lines: CartLine[], replaceCart: (lines: CartLine[]) => void, cash: OpenCashSession | null) {
  const { http } = useRuntime(); const { tenantId, branchId, access, session } = useWorkspace()
  const api = useMemo(() => http ? salesApi(http) : null, [http])
  const client = useQueryClient(); const [parameters, setParameters] = useSearchParams()
  const parametersRef = useRef(parameters)
  useEffect(() => { parametersRef.current = parameters }, [parameters])
  // UUID text is case-insensitive, just like the API resource identity.
  const saleId = (parameters.get('sale') ?? '').toLowerCase()
  const creationUnknown = parameters.has('draftPending') && !saleId
  const voidPending = parameters.has('voidPending')
  const checkoutPending = parameters.has('confirmPending') || voidPending
  const [server, setServer] = useState<SaleState | null>(null)
  const [phase, setPhase] = useState<Phase>(saleId ? 'loading' : 'idle')
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const loadedId = useRef(''); const busy = useRef(false); const operation = useRef<AbortController | null>(null)
  const preparingRef = useRef(false); const [preparing, setPreparing] = useState(false)
  useEffect(() => () => operation.current?.abort(), [])
  const mark = useCallback((changes: Record<string, string | null>) => {
    // Router search-param updaters are not queued like React state. Async responses
    // must not overwrite the sale ID using a closure from before creation.
    const next = new URLSearchParams(parametersRef.current)
    for (const [key, value] of Object.entries(changes)) { if (value === null) next.delete(key); else next.set(key, value) }
    parametersRef.current = next
    setParameters(next, { replace: true })
  }, [setParameters])
  const read = useCallback(async (id: string, signal?: AbortSignal) => {
    if (!api) throw new ApiError(0, 'client.https_required')
    return client.fetchQuery({ queryKey: scopedKey({ tenantId, branchId }, 'sale', session.userId, access.membershipId, id),
      staleTime: 0, retry: false, queryFn: async ({ signal: querySignal }) => {
        const combined = signal ? AbortSignal.any([signal, querySignal]) : querySignal
        const state = await api.state({ tenantId, branchId }, id, combined)
        return state.status === 'draft' ? api.draft({ tenantId, branchId }, id, combined) : state
      },
    })
  }, [api, client, tenantId, branchId, session.userId, access.membershipId])
  useEffect(() => {
    if (!saleId || loadedId.current === saleId) return
    const controller = new AbortController()
    if (!isUuid(saleId)) {
      void Promise.resolve().then(() => { if (!controller.signal.aborted) { setError(new ApiError(400, 'client.invalid_sale_id')); setPhase('blocked') } })
    } else void read(saleId, controller.signal).then(state => {
      if (controller.signal.aborted) return
      loadedId.current = state.saleId; setServer(state); replaceCart(restoreCart(state)); setPhase('idle'); setError(null)
      setNotice('Venta recuperada del servidor. Consulta el buscador para conocer la disponibilidad actual.')
      mark({ savingDraft: null, draftPending: null })
    }).catch(failure => { if (!controller.signal.aborted) { setError(failure); setPhase('blocked') } })
    return () => controller.abort()
  }, [saleId, read, replaceCart, mark])

  const seller = !server || server.sellerMembershipId === access.membershipId
  const cashMatches = Boolean(cash && (!server || server.cashSessionId === cash.cashSessionId))
  const locked = preparing || creationUnknown || checkoutPending || phase === 'loading' || phase === 'creating' || phase === 'saving' || phase === 'blocked' || !seller || Boolean(server && server.status !== 'draft')
  const dirty = Boolean(server && !matchesSaved(lines, server))
  const writable = Boolean(api && cashMatches && seller && !cartError(lines) && !locked && phase === 'idle' && (!server || server.saleId === saleId))
  async function recoverCreation() {
    const requestId = parameters.get('draftPending')
    if (!http || busy.current || !requestId || !isUuid(requestId)) return
    busy.current = true; setPhase('loading'); setError(null); operation.current = new AbortController()
    try {
      const receipt = await salesApi(http).recoverCreation({ tenantId, branchId }, requestId, operation.current.signal)
      const result = await read(receipt.saleId, operation.current.signal)
      if (operation.current.signal.aborted) return
      // A creation receipt identifies the original sale, not its current lines. Preserve an unsaved local cart.
      loadedId.current = result.saleId; setServer(result); mark({ sale: result.saleId, draftPending: null })
      if (lines.length === 0) replaceCart(restoreCart(result))
      setPhase(result.status === 'draft' && result.lines.length > 0 && lines.length > 0 && !matchesSaved(lines, result) ? 'conflict' : 'idle')
      setNotice('Borrador original identificado y reconsultado. El carrito local se conserva si aún no estaba guardado; no se repitió ninguna escritura.')
    } catch (failure) { if (!operation.current.signal.aborted) { setError(failure); setPhase('uncertain') } }
    finally { busy.current = false }
  }
  async function create() {
    if (!api || !writable || server || saleId || creationUnknown || !lines.length || busy.current) return
    busy.current = true; operation.current = new AbortController(); setPhase('creating'); setError(null)
    // Persist the original intention before sending its idempotency key.
    // Unknown creation is recovered by its receipt, never by creating another intention.
    const requestId = crypto.randomUUID(); mark({ draftPending: requestId })
    try {
      const result = await api.create({ tenantId, branchId }, operation.current.signal, { id: requestId, branchId })
      if (operation.current.signal.aborted) return
      loadedId.current = result.saleId; setServer(result); mark({ sale: result.saleId, draftPending: null })
      if (result.status !== 'draft' || result.sellerMembershipId !== access.membershipId || result.cashSessionId !== cash?.cashSessionId) {
        throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      }
      setPhase('idle'); setNotice('Preparando la venta para cobrar…')
      return result
    } catch (failure) {
      if (operation.current.signal.aborted) return
      setError(failure)
      const known = failure instanceof ApiError && !failure.outcomeUnknown
      if (known) mark({ draftPending: null })
      setPhase(known && failure.status !== 401 && failure.status !== 403 ? 'idle' : 'uncertain')
    } finally { busy.current = false }
  }
  async function refresh() {
    const id = server?.saleId ?? saleId
    if (!isUuid(id) || busy.current) return
    busy.current = true; operation.current = new AbortController(); setPhase('loading')
    try {
      const state = await read(id, operation.current.signal)
      if (operation.current.signal.aborted) return
      loadedId.current = state.saleId
      setServer(state); setError(null); setPhase(matchesSaved(lines, state) ? 'idle' : 'conflict')
      if (matchesSaved(lines, state)) mark({ sale: state.saleId, savingDraft: null, draftPending: null })
      setNotice('Estado persistido consultado. Revisa las líneas y el total del servidor antes de continuar.')
      return state
    } catch (failure) { if (!operation.current.signal.aborted) { setError(failure); setPhase('blocked') } }
    finally { busy.current = false }
  }
  async function save(target = server) {
    if (!api || !target || !writable || matchesSaved(lines, target) || busy.current) return
    busy.current = true; operation.current = new AbortController(); setPhase('saving'); setError(null)
    const submitted = lines; const expectedVersion = target.version; const id = target.saleId
    mark({ sale: id, draftPending: null, savingDraft: String(expectedVersion) })
    try {
      const result = await api.replace({ tenantId, branchId }, id, expectedVersion, saleInputs(submitted), operation.current.signal)
      if (operation.current.signal.aborted) return
      setServer(result)
      if (result.status !== 'draft' || !matchesSaved(submitted, result)) { setPhase('conflict'); setNotice('El resultado guardado difiere del carrito. Revisa el estado persistido.'); return }
      setPhase('idle'); mark({ sale: id, draftPending: null, savingDraft: null }); setNotice('Venta preparada. Revisa los medios de pago antes de confirmar.')
      return result
    } catch (failure) {
      if (operation.current.signal.aborted) return
      setError(failure)
      if (!(failure instanceof ApiError) || failure.outcomeUnknown || failure.status === 409) {
        try {
          const state = await read(id, operation.current.signal)
          setServer(state)
          if (state.status === 'draft' && matchesSaved(submitted, state)) {
            setPhase('idle'); setError(null); mark({ sale: id, draftPending: null, savingDraft: null }); setNotice('Venta preparada y verificada. No se repitió la escritura.')
            return state
          } else { setPhase('conflict'); setNotice('No se confirmó tu edición. Conservamos tu carrito; compara con el borrador persistido antes de resolver.'); }
        } catch { setPhase('uncertain') }
      } else {
        mark({ savingDraft: null }); setPhase(failure.status === 401 || failure.status === 403 ? 'blocked' : 'idle')
      }
    } finally { busy.current = false }
  }
  async function prepare() {
    if (preparingRef.current || !writable || creationUnknown || !lines.length) return false
    preparingRef.current = true; setPreparing(true)
    try {
      const target = server ?? await create()
      if (!target || operation.current?.signal.aborted) return false
      return matchesSaved(lines, target) || Boolean(await save(target))
    } finally { preparingRef.current = false; if (!operation.current?.signal.aborted) setPreparing(false) }
  }
  function nextSale() {
    if (busy.current || preparingRef.current || checkoutPending || !server || server.status === 'draft') return false
    loadedId.current = ''; setServer(null); replaceCart([]); setError(null); setNotice(''); setPhase('idle')
    mark({ sale: null, savingDraft: null, draftPending: null, confirmPending: null, voidPending: null })
    return true
  }
  function resolve(useServer: boolean) {
    if (!server || phase !== 'conflict') return
    if (useServer) replaceCart(restoreCart(server))
    setError(null); setPhase('idle'); mark({ savingDraft: null })
    setNotice(useServer ? 'Carrito reemplazado por la venta guardada.' : 'Carrito conservado. Pulsa Cobrar para preparar la venta con la versión consultada.')
  }
  async function recover(id: string) {
    if (!isUuid(id) || busy.current || checkoutPending) return
    busy.current = true; operation.current = new AbortController(); setPhase('loading')
    try {
      const state = await read(id, operation.current.signal)
      if (operation.current.signal.aborted) return
      loadedId.current = state.saleId; setServer(state); replaceCart(restoreCart(state))
      setError(null); setPhase('idle'); mark({ sale: state.saleId, draftPending: null, savingDraft: null })
      setNotice('Venta recuperada del servidor. El carrito se reemplazó por las líneas guardadas.')
    } catch (failure) { if (!operation.current.signal.aborted) { setError(failure); setPhase('blocked') } }
    finally { busy.current = false }
  }
  useNavigationProtection({ dirty: lines.length > 0 && (!server || dirty), busy: preparing || ['creating', 'saving', 'loading'].includes(phase), pending: creationUnknown || checkoutPending })
  return { recoverCreation, canRecoverCreation: isUuid(parameters.get('draftPending') ?? ''), server, phase, error, notice, seller, cashMatches, locked, dirty, creationUnknown, create, save, refresh, resolve, recover, hasKnownId: isUuid(saleId),
    prepare, preparing, nextSale, canPrepare: writable && !creationUnknown && lines.length > 0,
    checkoutPending, voidPending, markCheckout: (pending: boolean, kind: 'confirm' | 'void' = 'confirm') =>
      mark(pending ? { [kind === 'void' ? 'voidPending' : 'confirmPending']: '1' } : { confirmPending: null, voidPending: null }),
    canCheckout: writable && Boolean(server && server.status === 'draft' && server.lines.length) && !dirty,
    canCreate: writable && !server && !saleId && !creationUnknown && lines.length > 0,
    canSave: writable && Boolean(server) && dirty,
  }
}
export type DraftController = ReturnType<typeof useSaleDraft>
