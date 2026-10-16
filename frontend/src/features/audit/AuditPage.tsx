import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Panel } from '../../shared/ui/Panel'
import { auditApi, validPeriod } from './api'
import type { AuditFilters } from './api'
import '../../shared/styles/operations.css'

export function AuditPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Esta sección requiere otro acceso</h1><p>Auditoría requiere Owner.</p></Panel>
  return <AuditWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function AuditWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime()
  const [draft, setDraft] = useState(() => { const end = new Date(); return { from: new Date(end.getTime() - 86400000).toISOString().slice(0, 16), to: end.toISOString().slice(0, 16), entityId: '' } })
  const filters = (): AuditFilters => ({ fromUtc: draft.from + ':00Z', toUtc: draft.to + ':00Z', entityId: draft.entityId.trim(), offset: 0 })
  const [applied, setApplied] = useState(filters)
  const query = useQuery({ queryKey: scopedKey(w, 'audit', w.session.userId, w.access.membershipId, applied), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return auditApi(http).list(w, applied, signal) }, enabled: Boolean(http), retry: false, refetchOnWindowFocus: false })
  const rows = query.isSuccess && !query.isFetching ? query.data : null
  return <div className="operations"><PageHeader title="Auditoría" description="Metadatos de historial · Owner. Sin acceso a contenidos internos before/after." /><Panel><div className="operation-body"><form onSubmit={e => { e.preventDefault(); if (validPeriod(filters())) { setApplied(filters()); if (JSON.stringify(filters()) === JSON.stringify(applied)) void query.refetch() } }}>
    <TextField label="Desde UTC" type="datetime-local" required value={draft.from} onChange={e => setDraft({ ...draft, from: e.target.value })} /><TextField label="Hasta UTC" type="datetime-local" required value={draft.to} onChange={e => setDraft({ ...draft, to: e.target.value })} /><TextField label="Identificador de entidad (opcional)" value={draft.entityId} onChange={e => setDraft({ ...draft, entityId: e.target.value })} /><p>Los campos representan UTC, no hora de Lima. Intervalo mayor que cero y máximo 31 días.</p><Button type="submit" disabled={!http || !validPeriod(filters()) || query.isFetching}>Consultar historial</Button>
  </form></div></Panel><Panel><div className="operation-body"><h2>Historial · consulta aplicada</h2><p>{applied.fromUtc} → {applied.toUtc}</p>{query.isFetching && <p role="status">Consultando auditoría…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} />}{rows && <>{!rows.length && <p>No hay registros en esta página.</p>}<div className="operation-table"><table><caption>Metadatos registrados por el servidor</caption><thead><tr>{['Fecha UTC', 'Actor', 'Acción', 'Tipo', 'Entidad', 'Correlación', 'Estado anterior', 'Estado posterior'].map(name => <th key={name}>{name}</th>)}</tr></thead><tbody>{rows.map(row => <tr key={row.auditId}><td>{row.occurredAt}</td><td>{row.actorId}</td><td>{row.action}</td><td>{row.entityType}</td><td>{row.entityId}</td><td>{row.correlationId}</td><td>{row.hasBefore ? 'Existe' : 'No existe'}</td><td>{row.hasAfter ? 'Existe' : 'No existe'}</td></tr>)}</tbody></table></div></>}
    <PageControls offset={applied.offset} count={rows?.length} busy={query.isFetching} change={offset => setApplied({ ...applied, offset })} refresh={() => void query.refetch()} />
  </div></Panel></div>
}
