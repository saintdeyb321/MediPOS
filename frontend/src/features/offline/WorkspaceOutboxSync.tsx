import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useRuntime, useWorkspace } from '../access'
import { synchronizeOutbox } from './workflow'
import { offlineStorage } from './storage'

// Mounted for the authenticated workspace, independent of the selected module.
export function WorkspaceOutboxSync() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient()
  const { tenantId, branchId } = w; const userId = w.session.userId; const membershipId = w.access.membershipId
  const [pending, setPending] = useState(0); const [failed, setFailed] = useState(false)
  useEffect(() => {
    if (!http) return
    const abort = new AbortController(); const scope = { tenantId, branchId, userId, membershipId }; let running = false
    const sync = async () => {
      if (running || abort.signal.aborted || !navigator.onLine) return
      running = true
      try {
        const rows = await offlineStorage.outbox(scope, true)
        if (abort.signal.aborted) return
        setPending(rows.filter(r => r.status === 'pending').length)
        if (!rows.some(r => r.status === 'pending')) { setFailed(false); return }
        const changed = await synchronizeOutbox(scope, service, http, abort.signal)
        if (!abort.signal.aborted) {
          setFailed(false); setPending((await offlineStorage.outbox(scope, true)).filter(r => r.status === 'pending').length)
          if (changed) await client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === tenantId && q.queryKey[2] === branchId && ['stock', 'inventory-lots', 'product-search', 'current-cash'].includes(String(q.queryKey[3])) })
        }
      } catch { if (!abort.signal.aborted) setFailed(true) }
      finally { running = false }
    }
    void sync(); const timer = window.setInterval(() => void sync(), 10_000); window.addEventListener('online', sync)
    return () => { abort.abort(); clearInterval(timer); window.removeEventListener('online', sync) }
  }, [http, service, client, tenantId, branchId, userId, membershipId])
  return pending || failed ? <aside className="notice warning" role="status">{pending} eventos locales por verificar.{failed && ' No se confirmó la sincronización; se conservan los UUID.'} <Link to={w.base + '/sincronizacion'}>Revisar cola y conflictos</Link></aside> : null
}
