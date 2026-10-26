import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { Button } from '../../shared/ui/Button'
import { MetricGrid } from '../../shared/ui/MetricCard'
import { decimalLabel } from '../../shared/api/decimal'
import { reportsApi } from './api'
import { initialFilters } from './filters'

export function OwnerDashboard() {
  const w = useWorkspace()
  return w.access.role === 'owner' ? <Metrics key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} /> : null
}
function Metrics() {
  const w = useWorkspace(); const { http } = useRuntime()
  const filters = { ...initialFilters(w.branchId), kind: 'dashboard' as const, period: 'day' as const }
  const dashboard = useQuery({ queryKey: scopedKey(w, 'home-dashboard', w.session.userId, w.access.membershipId, 'day', filters.from), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return reportsApi(http).read(w, filters, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const data = !dashboard.isError && !dashboard.isFetching && dashboard.data?.kind === 'dashboard' ? dashboard.data.data : null
  return <section className="home-metrics" aria-label="Resumen de hoy">
    <div className="section-heading"><h2>Hoy en tu sucursal</h2><Link className="text-link" to={w.base + '/reportes'}>Ver reportes</Link></div>
    {dashboard.isFetching && <p role="status" className="data-caption">Consultando indicadores…</p>}
    {dashboard.isError && <ApiErrorState error={dashboard.error} retry={() => void dashboard.refetch()} title="No pudimos consultar los indicadores" />}
    {data && <><MetricGrid metrics={[
      ['Ventas netas de hoy', `S/ ${decimalLabel(data.metrics.netSalesAmount, 2)}`], ['Ventas confirmadas', decimalLabel(data.metrics.confirmedSaleCount)], ['Cajas abiertas', decimalLabel(data.metrics.openCashSessionCount)], ['Lotes próximos a vencer', decimalLabel(data.metrics.expiringLotCount)],
    ]} /><p className="data-caption">{data.period.fromLocalDate} · Lima. Cajas y lotes son el estado actual; ventas corresponden al periodo.</p></>}
    <Button variant="quiet" disabled={dashboard.isFetching} onClick={() => void dashboard.refetch()}>Actualizar</Button>
  </section>
}