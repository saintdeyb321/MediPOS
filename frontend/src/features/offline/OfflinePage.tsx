import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { TextField } from '../../shared/ui/TextField'
import { scopedKey } from '../../shared/api/contracts'
import { offlineApi } from './api'
import type { SyncConflict } from './api'
import { offlineStorage } from './storage'
import type { OfflineLease, OutboxRow } from './storage'
import { prepareOffline, synchronizeOutbox } from './workflow'

export function OfflinePage() {
  const w = useWorkspace()
  return <OfflineWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function OfflineWorkspace() {
  const w = useWorkspace(); const { service, http } = useRuntime()
  const scope = { userId: w.session.userId, membershipId: w.access.membershipId, tenantId: w.tenantId, branchId: w.branchId }
  const [lease, setLease] = useState<OfflineLease | null>(null); const [rows, setRows] = useState<OutboxRow[]>([])
  const [revocation, setRevocation] = useState<string | null>(null)
  const [busy, setBusy] = useState(false); const running = useRef(false); const mounted = useRef(true); const controller = useRef<AbortController | null>(null)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState('')
  const refresh = useCallback(async () => {
    const current = await offlineStorage.lease(); const pending = await offlineStorage.outbox(scope, true); const revoking = await offlineStorage.revocation(scope)
    if (mounted.current) { setRevocation(revoking); setLease(current?.userId === scope.userId && current.tenantId === scope.tenantId && current.branchId === scope.branchId && current.membershipId === scope.membershipId ? current : null); setRows(pending) }
  // Scope is immutable for this mounted workspace.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scope.userId, scope.tenantId, scope.branchId, scope.membershipId])
  useEffect(() => { mounted.current = true; void refresh().catch(setError); return () => { mounted.current = false; controller.current?.abort() } }, [refresh])
  async function run(kind: 'prepare' | 'sync' | 'revoke', manual = true) {
    if (!http || running.current) return
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController()
    try {
      if (kind === 'prepare') { await prepareOffline(w, service, http, controller.current.signal); if (mounted.current) setNotice('Catálogo, sesión, acceso y caja verificados. La contingencia vence como máximo con esta sesión, sin renovación offline.') }
      else if (kind === 'sync') { const sent = await synchronizeOutbox(scope, service, http, controller.current.signal, manual); if (mounted.current && (sent || manual)) setNotice(sent ? 'Lote conciliado por sus UUID originales. Solo «Aceptada» acredita confirmación del servidor.' : 'No se enviaron eventos: no hay pendientes elegibles o esta cola ya está siendo conciliada en otra pestaña.') }
      else if (revocation || lease) {
        const id = revocation ?? lease!.device.deviceId
        await offlineStorage.markRevocation(scope, id); await offlineStorage.lock(); if (mounted.current) { setLease(null); setRevocation(id) }
        // Revoke is server-idempotent for this exact device, including its audit. Never register a replacement.
        await offlineApi(http).revoke(w, id, controller.current.signal); await offlineStorage.markRevocation(scope, null)
        if (mounted.current) setNotice('Dispositivo revocado. Los pendientes se conservan aislados para revisión; no se enviarán como otro dispositivo.')
      }
      await refresh()
    } catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  const runRef = useRef(run); useEffect(() => { runRef.current = run })
  useEffect(() => {
    const sync = () => { if (navigator.onLine) void runRef.current('sync', false) }
    const timer = window.setInterval(sync, 10_000); window.addEventListener('online', sync)
    return () => { clearInterval(timer); window.removeEventListener('online', sync) }
  }, [])
  return <div className="operations"><PageHeader title="Contingencia y sincronización" description={`${w.branch.name} · sesión online verificada. Nunca administración, transferencias o edición de stock offline.`} />
    <Panel><div className="operation-body"><h2>Preparar este navegador</h2><p>Requiere caja propia abierta y conexión. Descarga solo el catálogo de esta sucursal; el stock es orientativo. Una desconexión no permite comprobar revocaciones ni asegurar stock entre dispositivos. El servidor vuelve a validar acceso, horario, precio, caja y existencias al sincronizar.</p>
      <Button disabled={busy || !http || Boolean(revocation)} onClick={() => void run('prepare')}>Preparar / actualizar contingencia</Button>
      {lease && <><p>Preparada para {lease.branchName}, hasta {new Date(lease.expiresAt).toISOString()}. Claves de sesión y CSRF nunca se guardan en IndexedDB.</p><Link className="button secondary" to="/contingencia">Abrir POS de contingencia</Link><Button variant="quiet" disabled={busy} onClick={() => void run('revoke')}>Revocar este dispositivo</Button></>}
      {revocation && <><p>Revocación original pendiente. La contingencia local está bloqueada; no se permite otra preparación ni reemplazar el dispositivo para ocultarla.</p><Button variant="secondary" disabled={busy || !http} onClick={() => void run('revoke')}>Conciliar revocación del mismo dispositivo</Button></>}
      {notice && <p role="status">{notice}</p>}{error != null && <ApiErrorState error={error} title="No pudimos completar la sincronización" />}
    </div></Panel>
    <Panel><div className="operation-body"><h2>Cola de esta cuenta y sucursal</h2><p>No borres los datos del navegador mientras existan pendientes. Al cerrar sesión o cambiar contexto se limpia el catálogo; los pendientes quedan aislados hasta verificar nuevamente esta misma cuenta. Reintentos automáticos limitados a seis por evento, con espera creciente; una respuesta perdida no cambia UUID ni payload.</p><Button disabled={busy || !http} onClick={() => void run('sync')}>Conciliar pendientes originales</Button><OutboxTable rows={rows} base={w.base} /></div></Panel>
    {http && <Conflicts key={scope.membershipId} />}
  </div>
}
export function OutboxTable({ rows, base }: { rows: OutboxRow[]; base?: string }) {
  const labels = { pending: 'Pendiente: sin confirmar', accepted: 'Aceptada por el servidor', conflict: 'Conflicto: no se registró venta', rejected: 'Rechazada: requiere revisión' }
  if (!rows.length) return <p>No hay eventos en esta cuenta y sucursal.</p>
  return <div className="operation-table"><table><caption>Eventos locales y respuestas del servidor</caption><thead><tr><th>Fecha UTC declarada</th><th>Estado</th><th>Resultado</th><th>Intentos</th></tr></thead><tbody>{rows.map(row => <tr key={row.id}><td>{row.envelope.clientOccurredAtUtc}</td><td>{labels[row.status]}</td><td>{row.result?.resultCode ?? 'Sin respuesta definitiva'}{base && row.status === 'accepted' && row.result?.resultReferenceId && <Link to={base + '/ventas?sale=' + row.result.resultReferenceId}>Consultar venta y ticket con conexión</Link>}</td><td>{row.attempts}</td></tr>)}</tbody></table></div>
}
function Conflicts() {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0); const [status, setStatus] = useState<'open' | 'resolved'>('open')
  const [error, setError] = useState<unknown>(null); const [writing, setWriting] = useState(false); const inFlight = useRef(false); const [uncertain, setUncertain] = useState(false)
  const [notice, setNotice] = useState(''); const controller = useRef<AbortController | null>(null)
  const query = useQuery({ queryKey: scopedKey(w, 'offline-conflicts', w.session.userId, w.access.membershipId, offset, status), queryFn: ({ signal }) => { if (!http) throw new Error('Se requiere HTTPS.'); return offlineApi(http).conflicts(w, offset, status, signal) }, enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const busy = writing || query.isFetching; const items = query.data?.items; const more = query.data?.hasMore
  useEffect(() => () => controller.current?.abort(), [])
  async function resolve(value: SyncConflict, code: 'cancelled' | 'reviewed_no_posting', note: string) {
    if (!http || busy || inFlight.current || uncertain || w.access.role !== 'owner') return
    inFlight.current = true; setWriting(true); setError(null); const abort = new AbortController(); controller.current = abort
    try { await offlineApi(http).resolve(w, value.id, code, note, abort.signal); if (!abort.signal.aborted) { setNotice('Conflicto resuelto sin crear ventas, pagos ni movimientos.'); await query.refetch() } }
    catch (failure) { if (!abort.signal.aborted) { setError(failure); setUncertain(true); setNotice('Consulta de nuevo el conflicto original. No se enviará otra resolución automáticamente.') } }
    finally { inFlight.current = false; if (!abort.signal.aborted) setWriting(false) }
  }
  return <Panel><div className="operation-body"><h2>Conflictos del servidor</h2><label>Estado<select value={status} disabled={busy} onChange={event => { setStatus(event.target.value as 'open' | 'resolved'); setOffset(0) }}><option value="open">Abiertos</option><option value="resolved">Resueltos</option></select></label>{busy && <p role="status">Consultando…</p>}{error != null && <ApiErrorState error={error} title="No pudimos completar la revisión de conflictos" />}{notice && <p role="status">{notice}</p>}
    {query.isError && <ApiErrorState error={query.error} title="No pudimos consultar conflictos" />}
    <Button variant="quiet" disabled={busy} onClick={() => { void query.refetch().then(result => { if (result.isSuccess) setUncertain(false) }) }}>Consultar conflictos originales</Button>
    {!busy && !error && items && !items.length && <p>No hay conflictos en esta página.</p>}
    {!error && items?.map(value => <ConflictRow key={value.id + '/' + value.status} value={value} allow={w.access.role === 'owner' && !busy && !uncertain} resolve={resolve} />)}
    <PageControls offset={offset} count={more ? 20 : 0} busy={busy} change={setOffset} refresh={() => void query.refetch()} />
  </div></Panel>
}
function ConflictRow({ value, allow, resolve }: { value: SyncConflict; allow: boolean; resolve: (v: SyncConflict, code: 'cancelled' | 'reviewed_no_posting', note: string) => Promise<void> }) {
  const [note, setNote] = useState(''); const [code, setCode] = useState<'cancelled' | 'reviewed_no_posting'>('reviewed_no_posting'); const [reviewed, setReviewed] = useState(false)
  return <section><h3>{value.conflictCode} · {value.status === 'open' ? 'Abierto' : 'Resuelto'}</h3><p>Ocurrencia declarada: {value.clientOccurredAtUtc}; recibido: {value.receivedAtUtc}. {value.lines.length} líneas. La resolución no fuerza stock ni confirma la venta.</p>{value.status === 'resolved' ? <p>{value.resolutionCode} · {value.resolutionNote}</p> : <><label>Resolución<select value={code} disabled={!allow} onChange={event => setCode(event.target.value as typeof code)}><option value="reviewed_no_posting">Revisado, sin contabilizar</option><option value="cancelled">Cancelado</option></select></label><TextField label="Nota de revisión del conflicto" value={note} disabled={!allow} maxLength={512} onChange={event => setNote(event.target.value)} /><label><input type="checkbox" disabled={!allow} checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Revisé el conflicto; esta resolución no crea otra venta.</label><Button disabled={!allow || !reviewed || !note.trim()} onClick={() => void resolve(value, code, note)}>Resolver conflicto</Button></>}</section>
}
