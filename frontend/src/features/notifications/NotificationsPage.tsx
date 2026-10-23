import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { Drawer } from '../../shared/ui/Modal'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import type { NotificationDetails } from './api'
import { notificationsApi } from './api'
import '../../shared/styles/operations.css'

export function NotificationsPage() {
  const w = useWorkspace()
  return <NotificationsWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.access.role}/${w.tenantId}/${w.branchId}`} />
}
function NotificationsWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const client = useQueryClient(); const api = useMemo(() => http ? notificationsApi(http) : null, [http])
  const [branchId, setBranchId] = useState(w.branchId); const [onlyUnread, setOnlyUnread] = useState(false); const [offset, setOffset] = useState(0)
  const [params, setParams] = useSearchParams(); const pending = params.get('notificationRead'); const [busy, setBusy] = useState(false); const running = useRef(false); const abort = useRef<AbortController | null>(null)
  const [error, setError] = useState<unknown>(null); const [notice, setNotice] = useState(''); const [denied, setDenied] = useState(false)
  const [selected, setSelected] = useState<NotificationDetails | null>(null)
  useEffect(() => () => abort.current?.abort(), [])
  const branches = useQuery({ queryKey: scopedKey(w, 'notification-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), enabled: Boolean(api) && w.access.role === 'owner', retry: false, refetchOnWindowFocus: false })
  const query = useQuery({ queryKey: scopedKey(w, 'notifications', w.session.userId, w.access.membershipId, branchId, onlyUnread, offset), queryFn: ({ signal }) => api!.list(w, branchId, onlyUnread, offset, signal), enabled: Boolean(api), retry: false, refetchOnWindowFocus: false })
  const count = useQuery({ queryKey: scopedKey(w, 'notification-count', w.session.userId, w.access.membershipId, branchId), queryFn: ({ signal }) => api!.count(w, branchId, signal), enabled: Boolean(api), retry: false, refetchOnWindowFocus: false })
  function mark(id: string | null) { const next = new URLSearchParams(params); if (id) next.set('notificationRead', id); else next.delete('notificationRead'); setParams(next, { replace: true }) }
  async function refresh() { await Promise.all([client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === w.tenantId && ['notifications', 'notification-count'].includes(String(q.queryKey[3])) })]) }
  async function read(id: string) {
    if (!api || running.current || pending || denied) return
    running.current = true; setBusy(true); setError(null); setNotice(''); mark(id); const controller = new AbortController(); abort.current = controller
    try { await api.read(w, id, controller.signal); if (controller.signal.aborted) return; mark(null); setNotice('Lectura registrada para tu membresía. Se reconsultaron la lista y el contador.'); await refresh() }
    catch (failure) { if (!controller.signal.aborted) { setError(failure); if (failure instanceof ApiError && !failure.outcomeUnknown) mark(null); if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true); await refresh() } }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  const rows = query.isSuccess && !query.isFetching ? query.data.rows : null
  return <div className="operations"><PageHeader title="Notificaciones" description="Lecturas personales de tu membresía. Una alerta leída puede continuar accionable." />
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    <Panel><div className="operation-body"><label>Sucursal de alertas<select aria-label="Sucursal de alertas" value={branchId} disabled={busy || w.access.role !== 'owner'} onChange={e => { setBranchId(e.target.value); setOffset(0) }}><option value={w.branchId}>{w.branch.name}</option>{w.access.role === 'owner' && branches.isSuccess && branches.data.length < 100 && <><option value="">Todas las sucursales autorizadas</option>{branches.data.filter(b => b.id !== w.branchId).map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</>}</select></label>
      {branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />}
      <label><input type="checkbox" checked={onlyUnread} disabled={busy} onChange={e => { setOnlyUnread(e.target.checked); setOffset(0) }} /> Solo no leídas</label>
      {count.isSuccess && !count.isFetching && <p>No leídas en el filtro: {count.data.literal}</p>}{count.isError && <ApiErrorState error={count.error} retry={() => void count.refetch()} />}
      {error != null && <ApiErrorState error={error} />}{notice && <p role="status">{notice}</p>}
      {pending && <Notice tone="warning">Lectura pendiente de conciliación; no se reenviará. Desactiva «Solo no leídas», consulta la página de la alerta y verifica su estado registrado. La ausencia de una fila no demuestra éxito.{rows?.some(r => r.notificationId === pending && r.isRead) && <Button disabled={busy} onClick={() => { mark(null); setError(null); void refresh() }}>Conciliar lectura registrada</Button>}</Notice>}
      {query.isFetching && <p role="status">Consultando notificaciones…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} />}
      {rows && <><p>Total del filtro: {query.data!.totalCount.literal}</p>{!rows.length && <p>No hay notificaciones en esta página.</p>}{rows.map(row => <article className="notification-row" key={row.notificationId}><div><h2>{row.title}</h2><p>{row.productName ?? 'Producto sin nombre disponible'} · Lote {row.batchNumber ?? 'No disponible'} · Vence <DateText value={row.expirationDate} dateOnly /></p><Badge tone={row.isRead ? 'neutral' : 'warning'}>{row.isRead ? 'Leída' : 'No leída'}</Badge> <Badge tone={row.isCurrentlyActionable ? 'warning' : 'neutral'}>{row.isCurrentlyActionable ? 'Requiere atención' : 'Histórica'}</Badge></div><div className="operation-actions"><Button variant="quiet" disabled={busy} onClick={() => setSelected(row)}>Ver alerta</Button><Button variant="secondary" disabled={busy || Boolean(pending) || denied || row.isRead} onClick={() => void read(row.notificationId)}>Marcar como leída</Button></div></article>)}</>}
      <PageControls offset={offset} count={rows && BigInt(query.data!.totalCount.literal) > BigInt(offset + rows.length) ? 20 : 0} busy={busy || query.isFetching} change={setOffset} refresh={() => void refresh()} />
    </div></Panel><Drawer open={selected !== null} title={selected?.title ?? 'Detalle de alerta'} onClose={() => setSelected(null)}>{selected && <div className="operation-body"><p>{selected.message}</p><dl className="summary-grid"><div><dt>Producto</dt><dd>{selected.productName ?? 'Sin nombre disponible'}</dd></div><div><dt>Lote</dt><dd>{selected.batchNumber ?? 'No disponible'}</dd></div><div><dt>Vencimiento</dt><dd><DateText value={selected.expirationDate} dateOnly /></dd></div><div><dt>Días restantes</dt><dd>{selected.daysUntilExpiration}</dd></div></dl><p>Sucursal: {selected.branchId === w.branchId ? w.branch.name : branches.data?.find(b => b.id === selected.branchId)?.name ?? 'No disponible en el directorio'}.</p><p>Creada <DateText value={selected.createdAt} />. {selected.isRead ? <>Leída <DateText value={selected.readAt} />.</> : 'No leída en esta consulta.'}</p><p>{selected.isCurrentlyActionable ? 'Actualmente accionable.' : 'Alerta histórica, ya no accionable.'} Marcar como leída no resuelve el riesgo del lote.</p></div>}</Drawer></div>
}
