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
  const checkoutPending = parameters.has('confirmPending')
  const [server, setServer] = useState<SaleState | null>(null)
  const [phase, setPhase] = useState<Phase>(saleId ? 'loading' : 'idle')
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const loadedId = useRef(''); const busy = useRef(false); const operation = useRef<AbortController | null>(null)
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
      setNotice('Borrador recuperado del servidor. Los precios guardados son snapshots; el stock y las presentaciones actuales se consultan en el buscador.')
      mark({ savingDraft: null, draftPending: null })
    }).catch(failure => { if (!controller.signal.aborted) { setError(failure); setPhase('blocked') } })
    return () => controller.abort()
  }, [saleId, read, replaceCart, mark])

  const seller = !server || server.sellerMembershipId === access.membershipId
  const cashMatches = Boolean(cash && (!server || server.cashSessionId === cash.cashSessionId))
  const locked = checkoutPending || phase === 'loading' || phase === 'creating' || phase === 'saving' || phase === 'blocked' || !seller || Boolean(server && server.status !== 'draft')
  const dirty = Boolean(server && !matchesSaved(lines, server))
  const writable = Boolean(api && cashMatches && seller && !cartError(lines) && !locked && phase === 'idle' && (!server || server.saleId === saleId))
  async function create() {
    if (!api || !writable || server || saleId || creationUnknown || !lines.length || busy.current) return
    busy.current = true; operation.current = new AbortController(); setPhase('creating'); setError(null)
    // This POST has no client-generated ID/idempotency key in the existing contract.
    // Keep a URL marker across reloads; unknown creation must never cause another POST.
    mark({ draftPending: '1' })
    try {
      const result = await api.create({ tenantId, branchId }, operation.current.signal)
      if (operation.current.signal.aborted) return
      loadedId.current = result.saleId; setServer(result); mark({ sale: result.saleId, draftPending: null })
      if (result.status !== 'draft' || result.sellerMembershipId !== access.membershipId || result.cashSessionId !== cash?.cashSessionId) {
        throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      }
      setPhase('idle'); setNotice('Borrador creado. Tu carrito sigue local: pulsa Guardar líneas para persistirlo. Conserva esta dirección para recuperarlo al recargar.')
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
  async function save() {
    if (!api || !server || !writable || !dirty || busy.current) return
    busy.current = true; operation.current = new AbortController(); setPhase('saving'); setError(null)
    const submitted = lines; const expectedVersion = server.version; const id = server.saleId
    mark({ sale: id, draftPending: null, savingDraft: String(expectedVersion) })
    try {
      const result = await api.replace({ tenantId, branchId }, id, expectedVersion, saleInputs(submitted), operation.current.signal)
      if (operation.current.signal.aborted) return
      setServer(result)
      if (result.status !== 'draft' || !matchesSaved(submitted, result)) { setPhase('conflict'); setNotice('El resultado guardado difiere del carrito. Revisa el estado persistido.'); return }
      setPhase('idle'); mark({ sale: id, draftPending: null, savingDraft: null }); setNotice('Líneas guardadas en el servidor. Es un borrador: no hay pagos ni descuento de stock.')
    } catch (failure) {
      if (operation.current.signal.aborted) return
      setError(failure)
      if (!(failure instanceof ApiError) || failure.outcomeUnknown || failure.status === 409) {
        try {
          const state = await read(id, operation.current.signal)
          setServer(state)
          if (state.status === 'draft' && matchesSaved(submitted, state)) {
            setPhase('idle'); setError(null); mark({ sale: id, draftPending: null, savingDraft: null }); setNotice('La lectura del mismo borrador confirma las líneas guardadas. No se repitió la escritura.')
          } else { setPhase('conflict'); setNotice('No se confirmó tu edición. Conservamos tu carrito; compara con el borrador persistido antes de resolver.'); }
        } catch { setPhase('uncertain') }
      } else {
        mark({ savingDraft: null }); setPhase(failure.status === 401 || failure.status === 403 ? 'blocked' : 'idle')
      }
    } finally { busy.current = false }
  }
  function resolve(useServer: boolean) {
    if (!server || phase !== 'conflict') return
    if (useServer) replaceCart(restoreCart(server))
    setError(null); setPhase('idle'); mark({ savingDraft: null })
    setNotice(useServer ? 'Carrito reemplazado por las líneas persistidas.' : 'Conservaste el carrito local. Guardar líneas enviará una edición explícita con la versión recién consultada.')
  }
  async function recover(id: string) {
    if (!isUuid(id) || busy.current || checkoutPending) return
    busy.current = true; operation.current = new AbortController(); setPhase('loading')
    try {
      const state = await read(id, operation.current.signal)
      if (operation.current.signal.aborted) return
      loadedId.current = state.saleId; setServer(state); replaceCart(restoreCart(state))
      setError(null); setPhase('idle'); mark({ sale: state.saleId, draftPending: null, savingDraft: null })
      setNotice('Borrador recuperado del servidor. El carrito se reemplazó por las líneas persistidas.')
    } catch (failure) { if (!operation.current.signal.aborted) { setError(failure); setPhase('blocked') } }
    finally { busy.current = false }
  }
  return { server, phase, error, notice, seller, cashMatches, locked, dirty, creationUnknown, create, save, refresh, resolve, recover, hasKnownId: isUuid(saleId),
    checkoutPending, markCheckout: (pending: boolean) => mark({ confirmPending: pending ? '1' : null }),
    canCheckout: writable && Boolean(server && server.status === 'draft' && server.lines.length) && !dirty,
    canCreate: writable && !server && !saleId && !creationUnknown && lines.length > 0,
    canSave: writable && Boolean(server) && dirty,
  }
}
export type DraftController = ReturnType<typeof useSaleDraft>
