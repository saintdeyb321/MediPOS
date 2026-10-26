import { BarChart } from '../../shared/ui/BarChart'
import { decimalLabel } from '../../shared/api/decimal'
import type { JsonNumber } from '../../shared/api/exactJson'
import type { ReportResult } from './contracts'

const money = (value: JsonNumber) => `S/ ${decimalLabel(value, 2)}`
export function ReportCharts({ result, branchName }: { result: ReportResult; branchName: (id: string) => string }) {
  switch (result.kind) {
    case 'dashboard': { const m = result.data.metrics; return <div className="analytics-grid">
      <BarChart title="Capital y exposición de inventario" description="Fotografía actual. El capital total incluye los importes vencidos y próximos; no se suman entre sí." rows={[
        { key: 'total', label: 'Capital físico total', value: m.currentInventoryCapitalAmount, formatted: money(m.currentInventoryCapitalAmount), tone: 'info' },
        { key: 'expiring', label: 'Próximo a vencer', value: m.expiringInventoryCapitalAmount, formatted: money(m.expiringInventoryCapitalAmount), tone: 'warning' },
        { key: 'expired', label: 'Vencido', value: m.expiredInventoryCapitalAmount, formatted: money(m.expiredInventoryCapitalAmount), tone: 'error' },
      ]} />
      <BarChart title="Señales para revisar" description="Conteos actuales del servidor. Productos críticos y lotes son indicadores distintos." rows={[
        { key: 'critical', label: 'Pares producto/sucursal críticos', value: m.criticalStockProductCount, formatted: decimalLabel(m.criticalStockProductCount), tone: 'warning' },
        { key: 'expiring', label: 'Lotes próximos a vencer', value: m.expiringLotCount, formatted: decimalLabel(m.expiringLotCount), tone: 'warning' },
        { key: 'expired', label: 'Lotes vencidos', value: m.expiredLotCount, formatted: decimalLabel(m.expiredLotCount), tone: 'error' },
      ]} />
    </div> }
    case 'overview': return <BarChart title="Ventas netas por sucursal" description="Mismo periodo aplicado. Las sucursales sin ventas conservan su valor cero." rows={result.data.branches.map(row => ({ key: row.branchId, label: row.branchName, value: row.netSalesAmount, formatted: money(row.netSalesAmount) }))} />
    case 'sales': return <BarChart title="Distribución del importe filtrado" description="Solo grupos de la página consultada; no es una serie temporal ni un total adicional." rows={result.data.rows.map(row => ({ key: row.groupId, label: row.groupName ?? 'Nombre no disponible', value: row.salesAmount, formatted: money(row.salesAmount) }))} />
    case 'product-rotation': return <BarChart title={result.data.mode === 'top' ? 'Productos con mayor importe' : 'Productos de menor actividad'} description="Página y orden del servidor. Cada importe corresponde al periodo aplicado." rows={result.data.rows.map(row => ({ key: row.businessProductId, label: row.productName, value: row.salesAmount, formatted: money(row.salesAmount), tone: 'info' }))} />
    case 'inventory-capital': return <BarChart title="Capital por sucursal" description="Saldo físico valorado por el servidor; incluye inventario vencido." rows={result.data.branches.map(row => ({ key: row.branchId, label: branchName(row.branchId), value: row.totalInventoryCapital, formatted: money(row.totalInventoryCapital), tone: 'info' }))} />
    case 'expirations': { const t = result.data.totals; return <BarChart title="Estado de los lotes" description="Totales del rango de vencimientos aplicado, no del periodo de ventas." rows={[
      { key: 'expired', label: 'Vencidos', value: t.expiredLotCount, formatted: decimalLabel(t.expiredLotCount), tone: 'error' },
      { key: 'expiring', label: 'Próximos', value: t.expiringLotCount, formatted: decimalLabel(t.expiringLotCount), tone: 'warning' },
      { key: 'later', label: 'Posteriores', value: t.laterLotCount, formatted: decimalLabel(t.laterLotCount), tone: 'success' },
    ]} /> }
    case 'stock-risk': return <BarChart title="Stock vendible por producto y sucursal" description="Solo la página consultada, en unidades base por producto. No se suman productos distintos." rows={result.data.rows.map(row => ({ key: row.branchId + row.businessProductId, label: row.productName + ' · ' + row.branchName, value: row.sellableStockBase, formatted: decimalLabel(row.sellableStockBase) + ' base', tone: row.isCritical ? 'warning' : 'info' }))} />
    case 'commissions': return <BarChart title="Comisiones del periodo" description="Original, compensado y neto son componentes relacionados; no son importes acumulables." rows={[
      { key: 'earned', label: 'Devengado original', value: result.data.totals.originalEarnedAmount, formatted: money(result.data.totals.originalEarnedAmount) },
      { key: 'reversed', label: 'Compensado', value: result.data.totals.compensatedAmount, formatted: money(result.data.totals.compensatedAmount), tone: 'warning' },
      { key: 'net', label: 'Neto actual', value: result.data.totals.currentNetAmount, formatted: money(result.data.totals.currentNetAmount), tone: 'success' },
    ]} />
  }
}
