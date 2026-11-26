import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { Bell, TriangleAlert } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { scopedKey } from '../../shared/api/contracts'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { PageHeader } from '../../shared/ui/PageHeader'
import { FilterBar } from '../../shared/ui/PageToolbar'
import { PageControls } from '../../shared/ui/PageControls'
import { DataSection } from '../../shared/ui/DataSection'
import { EmptyState } from '../../shared/ui/States'
import { Notice } from '../../shared/ui/Notice'
import { Drawer } from '../../shared/ui/Modal'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import type { NotificationDetails } from './api'
import { notificationCountStaleTime, notificationsApi } from './api'
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
  const branches = useQuery({ queryKey: scopedKey(w, 'notification-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), enabled: Boolean(api) && w.access.role === 'owner', retry: false, refetchOnWindowFocus: true })
  const query = useQuery({ queryKey: scopedKey(w, 'notifications', w.session.userId, w.access.membershipId, branchId, onlyUnread, offset), queryFn: ({ signal }) => api!.list(w, branchId, onlyUnread, offset, signal), enabled: Boolean(api), retry: false, refetchOnWindowFocus: true })
  const count = useQuery({ queryKey: scopedKey(w, 'notification-count', w.session.userId, w.access.membershipId, branchId), queryFn: ({ signal }) => api!.count(w, branchId, signal), enabled: Boolean(api), retry: false, staleTime: notificationCountStaleTime, refetchOnWindowFocus: true })
  function mark(id: string | null) { const next = new URLSearchParams(params); if (id) next.set('notificationRead', id); else next.delete('notificationRead'); setParams(next, { replace: true }) }
  function refresh() {
    return client.invalidateQueries({ predicate: q => q.queryKey[0] === 'tenant' && q.queryKey[1] === w.tenantId && ['notifications', 'notification-count'].includes(String(q.queryKey[3])) }, { throwOnError: true }).then(() => true, () => false)
  }
  async function read(id: string) {
    if (!api || running.current || pending || denied) return
    running.current = true; setBusy(true); setError(null); setNotice(''); mark(id); const controller = new AbortController(); abort.current = controller
    try {
      await api.read(w, id, controller.signal)
      if (controller.signal.aborted) return
      mark(null)
      const refreshed = await refresh()
      if (!controller.signal.aborted) setNotice(refreshed ? 'Lectura registrada para tu membresía.' : 'Lectura registrada para tu membresía. No pudimos actualizar la lista o el contador. Usa el reintento de la consulta que falló.')
    }
    catch (failure) { if (!controller.signal.aborted) { setError(failure); if (failure instanceof ApiError && !failure.outcomeUnknown) mark(null); if (failure instanceof ApiError && [401, 403].includes(failure.status)) setDenied(true); await refresh() } }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false) }
  }
  const rows = query.isSuccess && !query.isFetching ? query.data.rows : null
  const canChooseBranch = w.access.role === 'owner' && branches.isSuccess && branches.data.length > 1 && branches.data.length < 100
  const isFiltered = onlyUnread || (w.access.role === 'owner' && branchId !== '' && (!branches.isSuccess || branches.data.length > 1))
  return <div className="operations"><PageHeader title="Notificaciones" icon={Bell} eyebrow="" />
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    <DataSection className="notification-inbox"
      tabs={<nav className="task-tabs" aria-label="Lectura de notificaciones">
        <Button variant="quiet" aria-pressed={!onlyUnread} disabled={busy} onClick={() => { setOnlyUnread(false); setOffset(0) }}>Todas</Button>
        <Button variant="quiet" aria-pressed={onlyUnread} disabled={busy} onClick={() => { setOnlyUnread(true); setOffset(0) }}>
          No leídas {count.isSuccess && !count.isFetching && <Badge tone="info">{count.data.literal}</Badge>}
        </Button>
      </nav>}
      toolbar={canChooseBranch && <FilterBar label="Filtros de alertas"><label>Sucursal de alertas<select value={branchId} disabled={busy} onChange={e => {
        setBranchId(e.target.value); setOffset(0); setSelected(null)
      }}><option value={w.branchId}>{w.branch.name}</option><option value="">Todas las sucursales autorizadas</option>{branches.data.filter(b => b.id !== w.branchId).map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label></FilterBar>}
      footer={<>
        {rows && query.data && <p className="data-caption">Total del filtro: {query.data.totalCount.literal}</p>}
        <PageControls offset={offset} count={rows && BigInt(query.data!.totalCount.literal) > BigInt(offset + rows.length) ? 20 : 0} busy={busy || query.isFetching} change={setOffset} />
      </>}>
      {branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />}
      {count.isError && <ApiErrorState error={count.error} retry={() => void count.refetch()} />}
      {error != null && <ApiErrorState error={error} />}{notice && <p role="status">{notice}</p>}
      {pending && <Notice tone="warning">Lectura pendiente de conciliación; no se reenviará. Abre «Todas» y verifica el estado de la alerta en su página. La ausencia de una fila no demuestra éxito. <Button variant="secondary" disabled={busy || query.isFetching} onClick={() => void query.refetch()}>Consultar resultado de la lectura</Button>{rows?.some(r => r.notificationId === pending && r.isRead) && <Button disabled={busy} onClick={() => { mark(null); setError(null); void refresh() }}>Conciliar lectura registrada</Button>}</Notice>}
      {query.isFetching && <p role="status">Consultando notificaciones…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} />}
      {rows && (rows.length ? <div className="notification-list">{rows.map(row => <article className="notification-row" data-unread={!row.isRead} key={row.notificationId}>
        <div className="notification-type" aria-hidden="true">{row.isCurrentlyActionable ? <TriangleAlert size={19} /> : <Bell size={19} />}</div>
        <div className="notification-copy"><h2>{row.title}</h2><p className="notification-excerpt">{row.message}</p>
          <div className="notification-meta"><DateText value={row.createdAt} /><Badge tone={row.isRead ? 'neutral' : 'info'}>{row.isRead ? 'Leída' : 'No leída'}</Badge>
            <Badge tone={row.isCurrentlyActionable ? 'warning' : 'neutral'}>{row.isCurrentlyActionable ? 'Requiere atención' : 'Histórica'}</Badge></div>
        </div>
        <div className="operation-actions"><Button variant="quiet" disabled={busy} onClick={() => setSelected(row)}>Ver alerta</Button>
          <Button variant="secondary" disabled={busy || Boolean(pending) || denied || row.isRead} onClick={() => void read(row.notificationId)}>Marcar como leída</Button></div>
      </article>)}</div> : <EmptyState title={isFiltered ? 'No hay avisos con estos filtros' : offset > 0 ? 'No hay avisos en esta página' : 'Estás al día'}
        description={isFiltered ? canChooseBranch ? 'Cambia la vista o elige otra sucursal autorizada.' : 'Consulta la pestaña Todas para ver los avisos de tu sede.' : 'Los avisos de vencimiento aparecerán aquí.'} />)}
    </DataSection><Drawer open={selected !== null} title={selected?.title ?? 'Detalle de alerta'} onClose={() => setSelected(null)}>{selected && <div className="operation-body"><p>{selected.message}</p><dl className="summary-grid"><div><dt>Producto</dt><dd>{selected.productName ?? 'Sin nombre disponible'}</dd></div><div><dt>Lote</dt><dd>{selected.batchNumber ?? 'No disponible'}</dd></div><div><dt>Vencimiento</dt><dd><DateText value={selected.expirationDate} dateOnly /></dd></div><div><dt>Días restantes</dt><dd>{selected.daysUntilExpiration}</dd></div></dl><p>Sucursal: {selected.branchId === w.branchId ? w.branch.name : branches.data?.find(b => b.id === selected.branchId)?.name ?? 'No disponible en el directorio'}.</p><p>Creada <DateText value={selected.createdAt} />. {selected.isRead ? <>Leída <DateText value={selected.readAt} />.</> : 'No leída en esta consulta.'}</p><p>{selected.isCurrentlyActionable ? 'Actualmente accionable.' : 'Alerta histórica, ya no accionable.'} Marcar como leída no resuelve el riesgo del lote.</p></div>}</Drawer></div>
}
