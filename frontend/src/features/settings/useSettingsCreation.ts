import { useEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useSearchParams } from 'react-router'
import { isUuid, useRuntime } from '../access'
import { ApiError } from '../../shared/api/http'
import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { settingsApi } from './api'
import type { BranchDetails, BranchInput, LegalEntity, LegalInput, TenantSettingsScope } from './api'

type Pending = { kind: 'legal' | 'branch'; attempt?: string; acknowledgedId?: string; legalEntityId?: string; digest?: string }
type Recovery = { pending?: Pending; entityId?: string }
export type StructureRecord = LegalEntity | BranchDetails
const changeEvent = 'medipos-structure-changed'

async function fingerprint(attempt: string, fields: LegalInput | BranchInput) {
  const values = 'legalName' in fields ? [fields.legalName.trim(), fields.ruc.trim()] : [fields.legalEntityId, fields.name.trim()]
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify([attempt, ...values])))
  return Array.from(new Uint8Array(bytes), byte => byte.toString(16).padStart(2, '0')).join('')
}

function subscribe(change: () => void) {
  window.addEventListener('storage', change)
  window.addEventListener(changeEvent, change)
  return () => {
    window.removeEventListener('storage', change)
    window.removeEventListener(changeEvent, change)
  }
}

function read(key: string) {
  try { return localStorage.getItem(key) } catch { return '!' }
}

function parse(raw: string | null): Recovery | null {
  try {
    const value = JSON.parse(raw ?? '{}') as Recovery
    if (!value || typeof value !== 'object' || Array.isArray(value) || value.entityId !== undefined && !isUuid(value.entityId)) return null
    if (value.pending !== undefined && (!value.pending || !['legal', 'branch'].includes(value.pending.kind) ||
      !isUuid(value.pending.attempt) || value.pending.digest !== undefined && !/^[0-9a-f]{64}$/.test(value.pending.digest) || value.pending.legalEntityId !== undefined && !isUuid(value.pending.legalEntityId))) return null
    return value
  } catch { return null }
}

export function useSettingsCreation(scope: TenantSettingsScope, userId: string, membershipId: string) {
  const { http, service } = useRuntime()
  const [params, setParams] = useSearchParams()
  const address = useRef(params)
  useEffect(() => { address.current = params }, [params])
  const key = `medipos-structure-v1/${userId}/${membershipId}/${scope.tenantId}`
  const recovery = parse(useSyncExternalStore(subscribe, () => read(key)))
  // Previous versions kept the intention in the URL. It remains manual evidence.
  const legacyKind = params.get('settingsPending')
  const legacyId = params.get('settingsCreated')
  const legacy: Pending | undefined = !params.has('settingsAttempt') && (legacyKind === 'legal' || legacyKind === 'branch')
    ? { kind: legacyKind, acknowledgedId: isUuid(legacyId) ? legacyId : undefined } : undefined
  const pending = recovery?.pending ?? legacy
  const active = useRef<AbortController | null>(null)
  const [busy, setBusy] = useState(false)
  const [saving, setSaving] = useState<'legal' | 'branch'>()
  const [error, setError] = useState<unknown>(null)
  const [denied, setDenied] = useState(false)
  useEffect(() => () => active.current?.abort(), [])

  function updateAddress(intent?: Pending) {
    const next = new URLSearchParams(address.current)
    for (const field of ['settingsPending', 'settingsCreated', 'settingsAttempt']) next.delete(field)
    if (intent?.attempt) {
      next.set('settingsPending', intent.kind)
      next.set('settingsAttempt', intent.attempt)
    }
    address.current = next
    setParams(next, { replace: true })
  }
  function persist(value: Recovery) {
    try {
      if (value.pending || value.entityId) localStorage.setItem(key, JSON.stringify(value))
      else localStorage.removeItem(key)
    } catch { throw new ApiError(0, 'client.structure_recovery_unavailable') }
    updateAddress(value.pending)
    window.dispatchEvent(new Event(changeEvent))
  }

  const attemptInUrl = params.get('settingsAttempt')
  const storedAttempt = recovery?.pending?.attempt
  useEffect(() => {
    if (busy || !recovery || attemptInUrl === storedAttempt || !attemptInUrl && !storedAttempt) return
    // Restore a persisted address, or remove an address belonging to a different session.
    const next = new URLSearchParams(params)
    for (const field of ['settingsPending', 'settingsCreated', 'settingsAttempt']) next.delete(field)
    if (storedAttempt && recovery.pending) {
      next.set('settingsPending', recovery.pending.kind)
      next.set('settingsAttempt', storedAttempt)
    }
    setParams(next, { replace: true })
  }, [attemptInUrl, busy, params, recovery, setParams, storedAttempt])

  async function exclusive(work: (signal: AbortSignal) => Promise<void>) {
    if (active.current || denied) return
    if (!navigator.locks || !crypto.subtle || !recovery) { setError(new ApiError(0, 'client.structure_recovery_unavailable')); return }
    const controller = new AbortController()
    active.current = controller
    setBusy(true)
    setError(null)
    try {
      // One writer per botica in this browser. The recovery record remains scoped to its actor and membership.
      await navigator.locks.request('medipos-structure/' + scope.tenantId, { ifAvailable: true }, async lock => {
        controller.signal.throwIfAborted()
        if (!lock) throw new ApiError(409, 'client.structure_busy')
        await work(controller.signal)
      })
    } catch (failure) {
      if (controller.signal.aborted) return
      setError(failure)
      if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true)
    } finally {
      active.current = null
      if (!controller.signal.aborted) { setBusy(false); setSaving(undefined) }
    }
  }

  async function requireCurrentOwner(signal: AbortSignal) {
    const decision = await service.access(scope.tenantId, null, signal)
    const context = decision.context
    if (!decision.isAllowed || !context || context.userId !== userId || context.membershipId !== membershipId ||
      context.tenantId !== scope.tenantId || context.branchId !== null || context.role !== 'owner' || !context.membershipIsActive)
      throw new ApiError(403, decision.isAllowed ? 'access.context_changed' : decision.code)
  }

  async function run(input: LegalInput | BranchInput, options: {
    firstBranchName?: string
    before?: (signal: AbortSignal) => Promise<void>
    saved: (record: StructureRecord) => void
  }) {
    if (!http || pending) return
    await exclusive(async signal => {
      let current = parse(read(key))
      if (!current) throw new ApiError(0, 'client.structure_recovery_unavailable')
      if (current.pending) throw new ApiError(409, 'client.structure_pending')
      await options.before?.(signal)
      const api = settingsApi(http)
      async function create(fields: LegalInput | BranchInput) {
        setSaving('legalName' in fields ? 'legal' : 'branch')
        await requireCurrentOwner(signal)
        signal.throwIfAborted()
        const attempt = crypto.randomUUID()
        const digest = await fingerprint(attempt, fields)
        signal.throwIfAborted()
        const intent: Pending = 'legalName' in fields
          ? { kind: 'legal', attempt, digest }
          : { kind: 'branch', attempt, digest, legalEntityId: fields.legalEntityId }
        persist({ ...current, pending: intent })
        let result: StructureRecord
        try {
          result = 'legalName' in fields ? await api.createLegal(scope, fields, signal) : await api.createBranch(scope, fields, signal)
        } catch (failure) {
          if (!signal.aborted && failure instanceof ApiError && !failure.outcomeUnknown &&
            ([400, 401, 403, 404, 413, 415, 429].includes(failure.status) || failure.code === 'branch.limit_reached'))
            persist({ entityId: current?.entityId })
          throw failure
        }
        signal.throwIfAborted()
        // Update the view using the acknowledgement. A cache or follow-up read is not part of the write.
        options.saved(result)
        current = 'legalName' in result && options.firstBranchName ? { entityId: result.id } : {}
        persist(current)
        return result
      }

      const result = await create(input)
      if ('legalName' in result && options.firstBranchName)
        await create({ legalEntityId: result.id, name: options.firstBranchName })
    })
  }

  async function reconcile(record: StructureRecord, first: boolean, saved: (record: StructureRecord) => void) {
    const observed = pending
    if (!observed || record.tenantId !== scope.tenantId || observed.acknowledgedId && record.id !== observed.acknowledgedId ||
      (observed.kind === 'legal') !== ('legalName' in record) ||
      observed.legalEntityId && (!('legalEntityId' in record) || record.legalEntityId !== observed.legalEntityId)) return
    await exclusive(async signal => {
      const current = parse(read(key))
      if (!current || current.pending?.attempt !== observed.attempt) throw new ApiError(409, 'client.structure_pending')
      if (observed.attempt && observed.digest && await fingerprint(observed.attempt, record) !== observed.digest)
        throw new ApiError(409, 'client.structure_result_mismatch')
      signal.throwIfAborted()
      persist(first && 'legalName' in record ? { entityId: record.id } : {})
      saved(record)
    })
  }

  useNavigationProtection({ busy, pending: Boolean(pending) })
  return { pending, entityId: recovery?.entityId, busy, saving, error, denied, locked: !http || !recovery || busy || Boolean(pending) || denied, run, reconcile }
}
