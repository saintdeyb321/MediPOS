import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { ActionMenu } from '../../shared/ui/ActionMenu'
import { DateText } from '../../shared/ui/DateText'
import { reportsApi } from './api'
import { initialFilters, moreRows, reportLabels, validFilters } from './filters'
import { ReportFilters } from './ReportFilters'
import { reportView } from './view'
import '../../shared/styles/operations.css'

export function ReportsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert" className="state-block"><h1>Esta sección requiere otro acceso</h1><p>Reportes y comisiones requieren Owner con acceso vigente.</p><Link className="button secondary" to={w.base}>Volver al inicio</Link></Panel>
  return <ReportsWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function ReportsWorkspace() {
  const w = useWorkspace(); const { http, service } = useRuntime(); const api = useMemo(() => http ? reportsApi(http) : null, [http])
  const [draft, setDraft] = useState(() => initialFilters(w.branchId)); const [applied, setApplied] = useState(draft)
  const [exporting, setExporting] = useState(false); const [exportError, setExportError] = useState<unknown>(null); const [exportNotice, setExportNotice] = useState(''); const controller = useRef<AbortController | null>(null); const busy = useRef(false)
  useEffect(() => () => controller.current?.abort(), [])
  const branches = useQuery({ queryKey: scopedKey(w, 'report-branches', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => service.branches(w.tenantId, signal), enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const query = useQuery({ queryKey: scopedKey(w, 'owner-report', w.session.userId, w.access.membershipId, applied), queryFn: ({ signal }) => { if (!api) throw new ApiError(0, 'client.https_required'); return api.read(w, applied, signal) }, enabled: Boolean(api), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const view = !query.isError && !query.isFetching && query.data ? reportView(query.data, id => branches.data?.find(branch => branch.id === id)?.name ?? 'Sucursal no disponible en el directorio') : null
  async function download(format: 'pdf' | 'xlsx') {
    if (!api || busy.current || !view || applied.kind === 'overview' || applied.kind === 'dashboard') return
    busy.current = true; setExporting(true); setExportError(null); setExportNotice(''); const abort = new AbortController(); controller.current = abort
    try {
      const blob = await api.export(w, applied, format, abort.signal); if (abort.signal.aborted) return
      const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = `MediPOS-${applied.kind}.${format}`; document.body.append(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000)
      setExportNotice('Archivo generado con todos los filtros de la consulta aplicada, no solo esta página. Es una nueva lectura del servidor; puede reflejar cambios posteriores a la pantalla.')
    } catch (error) { if (!abort.signal.aborted) setExportError(error) } finally { busy.current = false; if (!abort.signal.aborted) setExporting(false) }
  }
  return <div className="operations"><PageHeader title="Reportes" description={`${w.tenantName} · métricas reales del servidor. Acceso exclusivo Owner.`} /><Link to={w.base + '/comisiones'}>Configurar comisiones y reglas</Link>
    {!http && <Notice tone="warning">Se requiere conexión HTTPS real.</Notice>}
    <Panel><div className="operation-body"><ReportFilters value={draft} branches={branches.isError ? [] : branches.data ?? []} complete={branches.isSuccess && branches.data.length < 100} disabled={exporting || !http} change={value => setDraft(current => ({ ...current, ...value, offset: 0 }))} apply={() => { if (validFilters(draft)) { setApplied({ ...draft, offset: 0 }); setExportError(null); setExportNotice(''); if (JSON.stringify(applied) === JSON.stringify({ ...draft, offset: 0 })) void query.refetch() } }} />{branches.isError && <ApiErrorState error={branches.error} retry={() => void branches.refetch()} title="No pudimos consultar sucursales" />}</div></Panel>
    <Panel aria-label="Resultado del reporte aplicado"><div className="operation-body"><h2>{reportLabels[applied.kind]} · consulta aplicada</h2>{query.isFetching && <p role="status">Consultando reporte…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} title="No pudimos consultar el reporte" />}
      {view && <><p>{view.period} · {applied.branchId ? branches.data?.find(branch => branch.id === applied.branchId)?.name ?? 'Sucursal seleccionada' : 'Todas las sucursales autorizadas'}</p><p>{view.explanation}</p>
        <dl className="summary-grid">{view.metrics.map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
        {!!view.columns.length && <div className="operation-table"><table><caption>{reportLabels[applied.kind]} · filas del servidor</caption><thead><tr>{view.columns.map(column => <th key={column}>{column}</th>)}</tr></thead><tbody>{view.rows.map(row => <tr key={row.key}>{row.cells.map((cell, index) => <td key={index}>{cell}</td>)}</tr>)}</tbody></table>{!view.rows.length && <p>No hay filas para los filtros de esta página. No se crearon operaciones para llenar el reporte.</p>}</div>}
        {view.page && <PageControls offset={applied.offset} count={moreRows(view.page.count, applied.offset, view.page.shown) ? 20 : 0} busy={exporting} change={offset => setApplied(current => ({ ...current, offset }))} refresh={() => void query.refetch()} />}
        {!view.page && <Button variant="quiet" disabled={exporting} onClick={() => void query.refetch()}>Actualizar reporte aplicado</Button>}
        <p>Última generación: <DateText value={query.data?.data.generatedAtUtc} /></p>
        {!['overview', 'dashboard'].includes(applied.kind) && <section aria-label="Exportar reporte"><ActionMenu label="Descargar consulta aplicada"><Button variant="quiet" disabled={exporting} onClick={() => void download('xlsx')}>Descargar Excel</Button><Button variant="quiet" disabled={exporting} onClick={() => void download('pdf')}>Descargar PDF</Button></ActionMenu><p>Se exportan los filtros aplicados completos, no solo la página visible. Máximo 10000 filas y 10 MiB.</p></section>}
      </>}{exporting && <p role="status">Generando archivo…</p>}{exportError != null && <ApiErrorState error={exportError} title="No pudimos descargar el reporte" />}{exportNotice && <p role="status">{exportNotice}</p>}
    </div></Panel>
  </div>
}
