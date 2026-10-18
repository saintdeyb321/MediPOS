import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiError } from '../../shared/api/http'
import type { HttpClient } from '../../shared/api/http'
import { isUuid } from '../access'
import { platformApi } from './api'
import type { LicenseAction, TrialInput, VerifiedIdentity } from './api'

export function usePlatformConsole(http: HttpClient, userId: string) {
  const api = useMemo(() => platformApi(http), [http]); const client = useQueryClient()
  const [params, setParams] = useSearchParams(); const latest = useRef(params); useEffect(() => { latest.current = params }, [params])
  const tenantId = params.get('tenant') ?? ''; const after = params.get('after') ?? undefined
  const key = ['platform', userId] as const
  const list = useQuery({ queryKey: [...key, 'tenants', after], queryFn: ({ signal }) => api.tenants(after, signal), retry: false, refetchOnWindowFocus: false })
  const detail = useQuery({ queryKey: [...key, 'tenant', tenantId], queryFn: ({ signal }) => api.tenant(tenantId, signal), enabled: isUuid(tenantId), retry: false, refetchOnWindowFocus: false })
  const categories = useQuery({ queryKey: [...key, 'categories'], queryFn: ({ signal }) => api.categories(signal), enabled: false, retry: false })
  const [categoriesRead, setCategoriesRead] = useState(false)
  const [identity, setIdentity] = useState<VerifiedIdentity | null>(null); const [busy, setBusy] = useState(false); const running = useRef(false)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState(''); const [denied, setDenied] = useState(false)
  const [reconciliationRead, setReconciliationRead] = useState(false)
  const abort = useRef<AbortController | null>(null)
  useEffect(() => () => abort.current?.abort(), [])
  useEffect(() => { return () => abort.current?.abort() }, [tenantId])
  function mark(values: Record<string, string | null>) {
    const next = new URLSearchParams(latest.current)
    for (const [name, value] of Object.entries(values)) { if (value) next.set(name, value); else next.delete(name) }
    latest.current = next; setParams(next, { replace: true })
  }
  async function perform(work: (signal: AbortSignal) => Promise<void>) {
    if (running.current || denied) return
    const controller = new AbortController(); abort.current = controller; running.current = true; setBusy(true); setError(null); setNotice('')
    try { await work(controller.signal) }
    catch (failure) {
      if (controller.signal.aborted) return
      setError(failure)
      if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true)
    } finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  async function send<T>(mutation: () => Promise<T>): Promise<T> {
    try { return await mutation() }
    catch (failure) {
      // Only a definitive POST rejection can retire its intent. A later GET 404 never proves absence of a write.
      if (failure instanceof ApiError && !failure.outcomeUnknown && failure.status >= 400 && failure.status < 500 && failure.status !== 409)
        mark({ platformPending: null, creationKey: null, categoryPending: null })
      throw failure
    }
  }
  const pending = params.get('platformPending'); const creationKey = params.get('creationKey')
  const categoryPending = params.get('categoryPending')
  const data = detail.isSuccess && !detail.isFetching ? detail.data : null
  const locked = busy || denied || Boolean(pending || creationKey || categoryPending)
  async function readCategories() {
    setCategoriesRead(false)
    await perform(async signal => {
      const rows = await api.categories(signal); signal.throwIfAborted()
      client.setQueryData([...key, 'categories'], rows); setCategoriesRead(true)
    })
  }
  async function createCategory(name: string) {
    if (locked || !categoriesRead || !categories.data || categories.data.length >= 100 ||
      categories.data.some(row => row.name.trim().toLocaleLowerCase('es-PE') === name.trim().toLocaleLowerCase('es-PE'))) return
    mark({ categoryPending: name.trim() }); setCategoriesRead(false)
    await perform(async signal => {
      const created = await send(() => api.createCategory(name.trim(), signal))
      const rows = await api.categories(signal); signal.throwIfAborted()
      client.setQueryData([...key, 'categories'], rows); setCategoriesRead(true)
      if (rows.filter(row => row.name.trim().toLocaleLowerCase('es-PE') === name.trim().toLocaleLowerCase('es-PE')).length > 1)
        throw new ApiError(409, 'client.category_ambiguous', undefined, true)
      if (!rows.some(row => row.id === created.id && row.name === created.name)) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      mark({ categoryPending: null }); setNotice('Categoría creada mediante el contrato oficial y reconsultada. No se creó ningún producto.')
    })
  }
  async function create(input: TrialInput) {
    if (locked || latest.current.has('creationKey')) return
    const requestKey = crypto.randomUUID(); mark({ creationKey: requestKey, platformPending: 'create' })
    await perform(async signal => {
      const created = await send(() => api.create(input, requestKey, signal))
      const receipt = await api.receipt(requestKey, signal)
      if (created.tenantId !== receipt.tenantId || created.licenseId !== receipt.licenseId) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      signal.throwIfAborted(); mark({ tenant: receipt.tenantId, creationKey: null, platformPending: null }); setNotice('Botica creada; resultado original conciliado mediante su constancia de idempotencia.')
      await client.invalidateQueries({ queryKey: [...key, 'tenants'] })
    })
  }
  async function reconcileCreation() {
    if (!creationKey) return
    await perform(async signal => {
      const receipt = await api.receipt(creationKey, signal); signal.throwIfAborted()
      mark({ tenant: receipt.tenantId, creationKey: null, platformPending: null }); setNotice('Resultado original recuperado. No se repitió la creación.')
      await client.invalidateQueries({ queryKey: [...key, 'tenants'] })
    })
  }
  async function lookup(id: string) {
    if (busy || denied || !isUuid(id) || locked && id !== data?.initialOwnerUserId) return
    await perform(async signal => { const verified = await api.identity(id, signal); signal.throwIfAborted(); setIdentity(verified) })
  }
  async function bootstrap() {
    if (locked || !data || !identity || data.initialOwnerUserId) return
    const observed = data; const owner = identity
    setReconciliationRead(false); mark({ platformPending: 'bootstrap' })
    await perform(async signal => {
      await send(() => api.bootstrap(observed.tenant.tenantId, owner.userId, signal))
      const current = await api.tenant(observed.tenant.tenantId, signal)
      if (current.initialOwnerUserId !== owner.userId) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      signal.throwIfAborted(); client.setQueryData([...key, 'tenant', observed.tenant.tenantId], current)
      mark({ platformPending: null }); setIdentity(null); setNotice('Primer Owner registrado y reconsultado. No se concedió autoridad de plataforma.')
    })
  }
  async function change(action: LicenseAction, input: { expiresAt: string } | { status: 'trial' | 'active' | 'grace' } | Record<string, never>) {
    if (locked || !data) return
    const observed = data; setReconciliationRead(false); mark({ platformPending: action })
    await perform(async signal => {
      await send(() => api.change(observed, action, input, signal))
      const current = await api.tenant(observed.tenant.tenantId, signal); signal.throwIfAborted()
      client.setQueryData([...key, 'tenant', observed.tenant.tenantId], current)
      mark({ platformPending: null }); setNotice('El servidor respondió y la licencia fue reconsultada. Los cambios conservan su auditoría.')
    })
  }
  async function refresh() {
    if (!isUuid(tenantId)) return
    setReconciliationRead(false)
    await perform(async signal => {
      const current = await api.tenant(tenantId, signal); signal.throwIfAborted()
      client.setQueryData([...key, 'tenant', tenantId], current); setReconciliationRead(true)
    })
  }
  return { list, detail, data, tenantId, identity, setIdentity, busy, locked, error, notice, pending, creationKey, denied, reconciliationRead, refresh,
    categories, categoriesRead, categoryPending, readCategories, createCategory,
    acknowledgeCategory: () => { if (!busy && categoryPending && categoriesRead && categories.data?.filter(row => row.name.trim().toLocaleLowerCase('es-PE') === categoryPending.trim().toLocaleLowerCase('es-PE')).length === 1) { mark({ categoryPending: null }); setNotice('Conciliación manual registrada en esta vista. No se repitió el POST ni se afirmó idempotencia de categorías.') } },
    select: (id: string) => { if (!locked) { setIdentity(null); setNotice(''); mark({ tenant: id }) } },
    page: (id: string | null) => { if (!locked) mark({ after: id }) },
    create, reconcileCreation, lookup, bootstrap, change,
    acknowledge: () => { if (!busy && data && reconciliationRead) { mark({ platformPending: null }); setReconciliationRead(false); setNotice('El operador concilió el resultado reconsultado; no se repitió ninguna escritura.') } },
  }
}
