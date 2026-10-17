import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { reportsApi } from './api'
import { initialFilters } from './filters'
import { reportView } from './view'

export function OwnerDashboard() {
  const w = useWorkspace()
  return w.access.role === 'owner' ? <Metrics key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} /> : null
}
function Metrics() {
  const w = useWorkspace(); const { http } = useRuntime(); const [period, setPeriod] = useState<'day' | 'week' | 'month'>('day')
  const filters = { ...initialFilters(w.branchId), kind: 'dashboard' as const, period }
  const dashboard = useQuery({ queryKey: scopedKey(w, 'home-dashboard', w.session.userId, w.access.membershipId, period), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return reportsApi(http).read(w, filters, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const overview = useQuery({ queryKey: scopedKey(w, 'home-overview', w.session.userId, w.access.membershipId, filters.from), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return reportsApi(http).read(w, { ...filters, kind: 'overview' }, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <Panel><div className="operation-body"><h2>Indicadores reales de la sucursal</h2><label>Periodo del dashboard<select value={period} onChange={event => setPeriod(event.target.value as typeof period)}><option value="day">Hoy</option><option value="week">Semana</option><option value="month">Mes</option></select></label>
    {([['Dashboard operacional', dashboard], ['Resumen de hoy', overview]] as const).map(([label, query]) => <section key={label}><h3>{label}</h3>{query.isFetching && <p role="status">Consultando {label.toLowerCase()}…</p>}{query.isError && <ApiErrorState error={query.error} retry={() => void query.refetch()} title={`No pudimos consultar ${label.toLowerCase()}`} />}
      {!query.isFetching && !query.isError && query.data && <><p>{reportView(query.data, () => w.branch.name).period} · generado UTC {query.data.data.generatedAtUtc}</p><dl>{reportView(query.data, () => w.branch.name).metrics.map(([key, value]) => <div key={key}><dt>{key}</dt><dd>{value}</dd></div>)}</dl><p>{reportView(query.data, () => w.branch.name).explanation}</p></>}
    </section>)}<Button variant="quiet" disabled={dashboard.isFetching || overview.isFetching} onClick={() => { void dashboard.refetch(); void overview.refetch() }}>Actualizar indicadores reales</Button></div></Panel>
}
