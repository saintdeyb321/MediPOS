import { useNavigationProtection } from '../../shared/ui/NavigationProtection'
import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router'
import { useRuntime } from '../access'
import { ApiError } from '../../shared/api/http'
import { settingsApi } from './api'
import type { BranchInput, LegalInput, TenantSettingsScope } from './api'

export function useSettingsCreation(w: TenantSettingsScope, refresh: () => Promise<unknown>) {
  const { http } = useRuntime(); const [params, setParams] = useSearchParams(); const latest = useRef(params); useEffect(() => { latest.current = params }, [params])
  const [busy, setBusy] = useState(false); const running = useRef(false); const abort = useRef<AbortController | null>(null); const [error, setError] = useState<unknown>(null); const [denied, setDenied] = useState(false); const [notice, setNotice] = useState('')
  useEffect(() => () => abort.current?.abort(), [])
  const pending = params.get('settingsPending'); const createdId = params.get('settingsCreated') ?? ''
  function mark(action: string | null, id = '') { const next = new URLSearchParams(latest.current); next.delete('settingsPending'); next.delete('settingsCreated'); if (action) next.set('settingsPending', action); if (id) next.set('settingsCreated', id); latest.current = next; setParams(next, { replace: true }) }
  async function run(input: LegalInput | BranchInput) {
    if (!http || running.current || latest.current.has('settingsPending') || denied) return
    const action = 'legalName' in input ? 'legal' : 'branch'
    running.current = true; setBusy(true); setError(null); setNotice(''); mark(action); const controller = new AbortController(); abort.current = controller
    try {
      const api = settingsApi(http); const result = 'legalName' in input ? await api.createLegal(w, input, controller.signal) : await api.createBranch(w, input, controller.signal)
      if (controller.signal.aborted) return
      mark(action, result.id); setNotice('Creación reconocida por el servidor. Revisa el registro en el directorio reconsultado y concilia antes de otra alta.'); await refresh()
    } catch (failure) {
      if (controller.signal.aborted) return
      setError(failure); if (failure instanceof ApiError && !failure.outcomeUnknown) mark(null)
      if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true)
      await refresh()
    } finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  function reconcile() { if (!running.current) { mark(null); setError(null); setNotice('Operación original conciliada por el operador contra el directorio. No se repitió la creación.') } }
  useNavigationProtection({ busy, pending: Boolean(pending) })
  return { pending, createdId, busy, error, notice, locked: busy || Boolean(pending) || denied, run, reconcile }
}
