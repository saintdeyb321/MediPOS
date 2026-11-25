import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiError } from '../../shared/api/http'
import type { HttpClient } from '../../shared/api/http'
import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { isUuid } from '../access'
import { cooldownDeadline, usePlatformCooldown } from './usePlatformCooldown'
import { platformApi } from './api'
import type { LicenseAction, TrialInput } from './api'

type Page = Awaited<ReturnType<ReturnType<typeof platformApi>['tenants']>>
type Administration = NonNullable<Page['items'][number]['administration']>
const canRevalidate = (query: { state: { status: string } }) => query.state.status === 'success'
const queryOptions = { retry: false, refetchOnWindowFocus: canRevalidate, refetchOnReconnect: canRevalidate, retryOnMount: false, staleTime: 60_000 } as const
const conflictCodes = new Set(['license.state_conflict', 'license.concurrent_change', 'platform.owner_invitation_conflict'])
const isDenied = (error: unknown) => error instanceof ApiError && [401, 403].includes(error.status)
export const categoryNameKey = (name: string) => name.trim().toLocaleLowerCase('es-PE')

export function usePlatformConsole(http: HttpClient, userId: string, sessionExpiry: string | undefined, area: string, admitted: boolean, showDetail: boolean, showOwner: boolean) {
  const api = useMemo(() => platformApi(http), [http])
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const latest = useRef(params)
  useEffect(() => { latest.current = params }, [params])

  const tenantId = params.get('tenant') ?? ''
  const after = area === 'home' ? undefined : params.get('after-' + area) ?? params.get('after') ?? undefined
  const pending = params.get('platformPending')
  const creationKey = params.get('creationKey')
  const categoryPending = params.get('categoryPending')
  const pendingLicense = pending === 'renew' || pending === 'suspend' || pending === 'reactivate'
  const hasPending = Boolean(pending || creationKey || categoryPending)
  const key = ['platform', userId, sessionExpiry ?? 'current'] as const
  const listKey = [...key, 'tenants']
  const detailKey = [...key, 'tenant', tenantId]
  const ownerKey = [...key, 'initial-owner', tenantId]
  const categoryKey = [...key, 'categories']
  const [authorityLost, setAuthorityLost] = useState(false)
  const canRead = admitted && !authorityLost
  const directory = ['home', 'businesses', 'owners', 'licenses'].includes(area)
  const list = useQuery({ ...queryOptions, queryKey: [...listKey, after], queryFn: ({ signal }) => api.tenants(after, signal), enabled: canRead && directory })
  const detail = useQuery({ ...queryOptions, queryKey: detailKey, queryFn: ({ signal }) => api.tenant(tenantId, signal), enabled: canRead && showDetail && isUuid(tenantId) })
  const initialOwner = useQuery({ ...queryOptions, queryKey: ownerKey, queryFn: ({ signal }) => api.initialOwner(tenantId, signal), enabled: canRead && showOwner && detail.isSuccess && isUuid(tenantId) })
  const categories = useQuery({ ...queryOptions, queryKey: categoryKey, queryFn: ({ signal }) => api.categories(signal), enabled: canRead && area === 'catalog' && !categoryPending })
  useEffect(() => {
    const scope = ['platform', userId, sessionExpiry ?? 'current']
    if (!canRead || !directory) void client.cancelQueries({ queryKey: [...scope, 'tenants'] })
    if (!canRead || !showDetail) void client.cancelQueries({ queryKey: [...scope, 'tenant'] })
    if (!canRead || !showOwner) void client.cancelQueries({ queryKey: [...scope, 'initial-owner'] })
    if (!canRead || area !== 'catalog') void client.cancelQueries({ queryKey: [...scope, 'categories'] })
  }, [area, canRead, client, directory, sessionExpiry, showDetail, showOwner, userId])
  const resourceDenied = [list.error, detail.error, initialOwner.error, categories.error].some(isDenied)
  const denied = authorityLost || resourceDenied
  useEffect(() => {
    if (!denied) return
    let active = true
    void client.cancelQueries({ queryKey: ['platform'] }).then(() => {
      if (!active) return
      if (!authorityLost) setAuthorityLost(true)
      else client.removeQueries({ queryKey: ['platform'] })
    })
    return () => { active = false }
  }, [authorityLost, client, denied])

  const [categoryReconciled, setCategoryReconciled] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<{ area: string; notice?: string; error?: unknown; failedAt?: number }>()
  const [reconciliationRead, setReconciliationRead] = useState(false)
  const abort = useRef<AbortController | null>(null)
  useEffect(() => () => abort.current?.abort(), [])
  const error = feedback?.area === area ? feedback.error : undefined
  const notice = feedback?.area === area ? feedback.notice : undefined
  const failedAt = feedback?.area === area ? feedback.failedAt ?? 0 : 0
  const categoriesRead = categories.isSuccess && !categories.isFetching && (!categoryPending || categoryReconciled === categoryPending)
  const data = detail.isSuccess && !detail.isFetching ? detail.data : null
  const cooldown = usePlatformCooldown(cooldownDeadline(error, failedAt), failedAt)
  const locked = busy || denied || !admitted || hasPending || cooldown > 0
  function mark(values: Record<string, string | null>) {
    const next = new URLSearchParams(latest.current)
    for (const [name, value] of Object.entries(values)) {
      if (value) next.set(name, value)
      else next.delete(name)
    }
    latest.current = next
    setParams(next, { replace: true })
  }
  function report(notice: string) { setFeedback({ area, notice }) }

  async function perform<T>(work: (signal: AbortSignal) => Promise<T>) {
    if (abort.current || denied || !admitted || cooldown > 0) return
    const controller = new AbortController()
    abort.current = controller
    setBusy(true)
    setFeedback(undefined)
    try { return await work(controller.signal) }
    catch (failure) {
      if (controller.signal.aborted) return
      setFeedback({ area, error: failure, failedAt: Date.now() })
      if (isDenied(failure)) setAuthorityLost(true)
    } finally {
      abort.current = null
      if (!controller.signal.aborted) setBusy(false)
    }
  }
  async function send<T>(mutation: () => Promise<T>, retireOnConflict?: () => void): Promise<T> {
    try { return await mutation() }
    catch (failure) {
      // Only the write response can prove rejection. Read failures always leave the original intent available.
      if (failure instanceof ApiError && !failure.outcomeUnknown && failure.status >= 400 && failure.status < 500) {
        if (failure.status === 409 && conflictCodes.has(failure.code)) retireOnConflict?.()
        if (failure.status !== 409 || conflictCodes.has(failure.code) && retireOnConflict) {
          mark({ platformPending: null, creationKey: null, categoryPending: null, ownerExpected: null })
        }
      }
      throw failure
    }
  }

  async function readCategories() {
    if (cooldown > 0) return
    setCategoryReconciled(null)
    await perform(async signal => {
      await client.cancelQueries({ queryKey: categoryKey })
      const rows = await api.categories(signal)
      signal.throwIfAborted()
      client.setQueryData(categoryKey, rows)
      setCategoryReconciled(categoryPending)
    })
  }
  async function createCategory(name: string) {
    if (locked || abort.current || !categoriesRead || !categories.data || categories.data.length >= 100 ||
      categories.data.some(row => categoryNameKey(row.name) === categoryNameKey(name))) return
    mark({ categoryPending: name.trim() })
    setCategoryReconciled(null)
    return perform(async signal => {
      await client.cancelQueries({ queryKey: categoryKey })
      const created = await send(() => api.createCategory(name.trim(), signal))
      signal.throwIfAborted()
      client.setQueryData<typeof categories.data>(categoryKey, rows =>
        [...(rows ?? []).filter(row => row.id !== created.id), created].sort((a, b) => a.id.localeCompare(b.id)).slice(0, 100))
      mark({ categoryPending: null })
      report('Categoría creada.')
      return true
    })
  }
  function confirmCreation(tenant: string, message: string) {
    mark({ tenant, creationKey: null, platformPending: null })
    report(message)
    void client.invalidateQueries({ queryKey: listKey })
  }
  async function create(input: TrialInput) {
    if (locked || abort.current || latest.current.has('creationKey')) return
    const requestKey = crypto.randomUUID()
    mark({ creationKey: requestKey, platformPending: 'create' })
    return perform(async signal => {
      const created = await send(() => api.create(input, requestKey, signal))
      const receipt = await api.receipt(requestKey, signal)
      if (created.tenantId !== receipt.tenantId || created.licenseId !== receipt.licenseId) throw new ApiError(0, 'client.outcome_unknown', undefined, true)
      signal.throwIfAborted()
      confirmCreation(receipt.tenantId, 'Botica creada. La constancia confirmó el alta.')
      return true
    })
  }
  async function reconcileCreation() {
    if (!creationKey) return
    return perform(async signal => {
      const receipt = await api.receipt(creationKey, signal)
      signal.throwIfAborted()
      confirmCreation(receipt.tenantId, 'Resultado original recuperado. No se repitió la creación.')
      return true
    })
  }
  async function cancelReads(resource: readonly unknown[]) {
    await Promise.all([client.cancelQueries({ queryKey: resource }), client.cancelQueries({ queryKey: listKey })])
  }
  function patchDirectory(fields: Partial<Administration>) {
    client.setQueriesData<Page>({ queryKey: listKey }, page => page && ({
      ...page, items: page.items.map(row => row.tenantId === tenantId && row.administration
        ? { ...row, administration: { ...row.administration, ...fields } } : row),
    }))
    void client.invalidateQueries({ queryKey: listKey, refetchType: 'none' })
  }
  async function change(action: LicenseAction, input: { expiresAt: string } | { status: 'trial' | 'active' | 'grace' } | Record<string, never>) {
    if (locked || abort.current || !data) return
    const observed = data
    setReconciliationRead(false)
    mark({ platformPending: action })
    return perform(async signal => {
      await cancelReads(detailKey)
      const updated = await send(() => api.change(observed, action, input, signal), () => {
        void client.invalidateQueries({ queryKey: detailKey })
      })
      signal.throwIfAborted()
      await cancelReads(detailKey)
      signal.throwIfAborted()
      client.setQueryData(detailKey, { ...observed, license: updated })
      patchDirectory({ licenseStatus: updated.status, startsAt: updated.startsAt, expiresAt: updated.expiresAt, maxBranches: updated.maxBranches })
      mark({ platformPending: null })
      report('El servidor confirmó el cambio de licencia.')
      return true
    })
  }
  async function refresh() {
    if (!isUuid(tenantId)) return
    setReconciliationRead(false)
    await perform(async signal => {
      await cancelReads(detailKey)
      await client.cancelQueries({ queryKey: ownerKey })
      const current = pending ? await api.tenant(tenantId, signal) : (await detail.refetch()).data
      signal.throwIfAborted()
      if (!current || !pending && client.getQueryState(detailKey)?.status !== 'success') return
      client.setQueryData(detailKey, current)
      patchDirectory({ licenseStatus: current.license.status, startsAt: current.license.startsAt, expiresAt: current.license.expiresAt, maxBranches: current.license.maxBranches })
      if (!pendingLicense) {
        const owner = pending ? await api.initialOwner(tenantId, signal) : (await initialOwner.refetch({ cancelRefetch: false })).data
        signal.throwIfAborted()
        if (pending && owner !== undefined) client.setQueryData(ownerKey, owner)
        // Refresh the member-derived owner columns only when the underlying state may have changed.
        if (pending) void client.invalidateQueries({ queryKey: listKey })
      }
      setReconciliationRead(true)
    })
  }
  async function changeOwner(email: string | null) {
    if (locked || abort.current || !data || data.initialOwnerUserId || !initialOwner.isSuccess || initialOwner.isFetching || initialOwner.data?.status !== 'pending') return
    const expected = initialOwner.data.invitationId
    mark({ platformPending: email === null ? 'owner-revoke' : 'owner-change', ownerExpected: expected })
    setReconciliationRead(false)
    return perform(async signal => {
      await cancelReads(ownerKey)
      const changed = await send(() => api.changeOwner(tenantId, expected, email, signal), () => {
        void client.invalidateQueries({ queryKey: ownerKey })
      })
      signal.throwIfAborted()
      await cancelReads(ownerKey)
      signal.throwIfAborted()
      client.setQueryData(ownerKey, changed)
      patchDirectory({ ownerStatus: changed.status, ownerEmail: changed.email, ownerName: null, ownerAssignedAt: changed.createdAt })
      mark({ platformPending: null, ownerExpected: null })
      report(email === null ? 'Asignación pendiente revocada.' : 'Gmail pendiente actualizado.')
      return true
    })
  }

  useNavigationProtection({ busy, pending: hasPending })
  return { list, detail, initialOwner, changeOwner, data, tenantId, after, busy, locked, error, notice, failedAt, cooldown, pending, creationKey, denied, admitted, reconciliationRead, refresh,
    pendingLicense, categories, categoriesRead, categoryPending, readCategories, createCategory,
    acknowledgeCategory: () => {
      if (!admitted || busy || !categoryPending || !categoriesRead || categories.data?.filter(row => categoryNameKey(row.name) === categoryNameKey(categoryPending)).length !== 1) return
      mark({ categoryPending: null })
      report('Conciliación manual registrada. No se repitió la creación.')
    },
    select: (id: string) => {
      if (busy || denied || hasPending) return
      setFeedback(undefined)
      setReconciliationRead(false)
      mark({ tenant: id })
    },
    page: (id: string | null) => {
      if (!busy && !denied) mark({ ['after-' + area]: id, after: null })
    },
    create, reconcileCreation, change,
    acknowledge: () => {
      if (!admitted || busy || !data || !reconciliationRead) return
      mark({ platformPending: null, ownerExpected: null })
      setReconciliationRead(false)
      report('Resultado conciliado sin repetir la escritura.')
    },
  }
}
