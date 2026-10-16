import type { Branch } from '../access'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { dimensions, periodKinds, sorts } from './contracts'
import type { Dimension, PeriodKind, ReportKind, SalesSort } from './contracts'
import { allowedSort, reportLabels, usesDates, validFilters } from './filters'
import type { ReportFilters as Filters } from './filters'
import { ReportSelectors } from './ReportSelectors'

const periodLabels = { day: 'Hoy', week: 'Semana actual (lunes–domingo)', month: 'Mes actual', year: 'Año actual', custom: 'Rango personalizado' }
const dimensionLabels = { branch: 'Sucursal', employee: 'Empleado', product: 'Producto', category: 'Categoría' }
const sortLabels = { sales_amount_desc: 'Importe: mayor a menor', sales_amount_asc: 'Importe: menor a mayor', sale_count_desc: 'Ventas: mayor a menor', sale_count_asc: 'Ventas: menor a mayor', product_name_asc: 'Nombre de producto', employee_name_asc: 'Nombre de empleado', branch_name_asc: 'Nombre de sucursal' }
export function ReportFilters({ value: f, branches, complete, disabled, change, apply }: { value: Filters; branches: readonly Branch[]; complete: boolean; disabled: boolean; change: (value: Partial<Filters>) => void; apply: () => void }) {
  return <section aria-label="Filtros de reportes"><h2>Consulta Owner</h2><p>Filtros en edición. Aplicar consulta el servidor; las cifras y exportación usan la última consulta aplicada. Fechas inclusivas de Lima.</p><fieldset disabled={disabled}><legend>Reporte y alcance</legend>
    <label htmlFor="report-kind">Reporte</label><select id="report-kind" value={f.kind} onChange={event => change({ kind: event.target.value as ReportKind, offset: 0 })}>{Object.entries(reportLabels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>
    <label htmlFor="report-branch">Sucursal del reporte</label><select id="report-branch" value={f.branchId} onChange={event => change({ branchId: event.target.value })}><option value="">Todas las sucursales autorizadas</option>{branches.map(branch => <option key={branch.id} value={branch.id}>{branch.name}</option>)}</select>
    {['dashboard', 'sales', 'product-rotation'].includes(f.kind) && <><label htmlFor="report-period">Periodo Lima</label><select id="report-period" value={f.period} onChange={event => change({ period: event.target.value as PeriodKind })}>{periodKinds.map(period => <option key={period} value={period}>{periodLabels[period]}</option>)}</select></>}
    {usesDates(f) && <><TextField type="date" label={f.kind === 'expirations' ? 'Vencimiento desde' : 'Desde (Lima)'} value={f.from} onChange={event => change({ from: event.target.value })} /><TextField type="date" label={f.kind === 'expirations' ? 'Vencimiento hasta' : 'Hasta (Lima)'} value={f.to} onChange={event => change({ to: event.target.value })} /><p>Máximo {f.kind === 'overview' || f.kind === 'commissions' ? '31' : '366'} días inclusivos.</p></>}
    {f.kind === 'sales' && <><label htmlFor="report-dimension">Agrupar ventas por</label><select id="report-dimension" value={f.dimension} onChange={event => change({ dimension: event.target.value as Dimension, sort: 'sales_amount_desc' })}>{dimensions.map(dimension => <option key={dimension} value={dimension}>{dimensionLabels[dimension]}</option>)}</select><label htmlFor="report-sort">Orden de ventas</label><select id="report-sort" value={f.sort} onChange={event => change({ sort: event.target.value as SalesSort })}>{sorts.filter(sort => allowedSort(f.dimension, sort)).map(sort => <option key={sort} value={sort}>{sortLabels[sort]}</option>)}</select></>}
    {f.kind === 'stock-risk' && <label><input type="checkbox" checked={f.criticalOnly} onChange={event => change({ criticalOnly: event.target.checked })} /> Solo críticos con mínimo configurado</label>}
    {f.kind === 'product-rotation' && <><label htmlFor="report-rotation">Rotación</label><select id="report-rotation" value={f.rotation} onChange={event => change({ rotation: event.target.value as Filters['rotation'] })}><option value="top">Mayor importe de ventas</option><option value="low_activity">Baja actividad (incluye cero)</option></select></>}
    <ReportSelectors filters={f} change={change} />
    {!validFilters(f) && <p role="alert">Revisa el rango de fechas y los filtros admitidos. No se envió una consulta inválida.</p>}{!complete && <p>Directorio de sucursales no disponible o incompleto; no se aplica un alcance sin verificar.</p>}
    <Button disabled={!validFilters(f) || !complete || Boolean(f.branchId && !branches.some(branch => branch.id === f.branchId))} onClick={apply}>Aplicar filtros y consultar</Button>
  </fieldset></section>
}
