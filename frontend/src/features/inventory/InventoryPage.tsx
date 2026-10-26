import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Boxes } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ProductLabel, ProductLookup } from '../catalog'
import type { ProductDetail } from '../catalog'
import { scopedKey } from '../../shared/api/contracts'
import { compare, decimalLabel, zero } from '../../shared/api/decimal'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { PageControls } from '../../shared/ui/PageControls'
import { Notice } from '../../shared/ui/Notice'
import { Badge } from '../../shared/ui/Badge'
import { DateText } from '../../shared/ui/DateText'
import { MetricGrid } from '../../shared/ui/MetricCard'
import { Modal } from '../../shared/ui/Modal'
import { PageToolbar } from '../../shared/ui/PageToolbar'
import { inventoryApi } from './api'
import type { LotStock } from './api'
import { useInventoryWrite } from './useInventoryWrite'
import { AdjustmentForm, MinimumForm } from './InventoryForms'
import '../../shared/styles/operations.css'

export function InventoryPage() {
  const w = useWorkspace()
  if (!['owner', 'pharmacist'].includes(w.access.role)) return <Notice tone="warning">Inventario requiere Owner o Pharmacist con acceso operacional vigente.</Notice>
  return <InventoryWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function InventoryWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime(); const write = useInventoryWrite()
  const [mode, setMode] = useState<'stock' | 'lots' | 'expiring'>('stock'); const [offset, setOffset] = useState(0)
  const [product, setProduct] = useState<ProductDetail | null>(null); const [lot, setLot] = useState<LotStock | null>(null)
  const [filterOpen, setFilterOpen] = useState(false); const [minimumOpen, setMinimumOpen] = useState(false)
  const [adjustmentOpen, setAdjustmentOpen] = useState(false)
  const [dirty, setDirty] = useState(false)
  const key = (resource: string) => scopedKey(w, resource, w.session.userId, w.access.membershipId, product?.product.id, offset)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return inventoryApi(http) }
  const stock = useQuery({ queryKey: key('stock'), queryFn: ({ signal }) => api().stock(w, product?.product.id, offset, 20, signal), enabled: Boolean(http && mode === 'stock'), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const lots = useQuery({ queryKey: key('inventory-lots'), queryFn: ({ signal }) => api().lots(w, product?.product.id, offset, 20, signal), enabled: Boolean(http && mode === 'lots'), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const expiring = useQuery({ queryKey: key('inventory-expiring'), queryFn: ({ signal }) => api().expiring(w, product?.product.id, offset, 20, signal), enabled: Boolean(http && mode === 'expiring'), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  const current = mode === 'stock' ? stock : mode === 'lots' ? lots : expiring
  const dateParts = new Intl.DateTimeFormat('en', { timeZone: 'America/Lima', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
  const today = ['year', 'month', 'day'].map(type => dateParts.find(part => part.type === type)!.value).join('-')
  return <div className="operations"><PageHeader icon={Boxes} eyebrow="Existencias · control por sucursal" title="Inventario" description={`${w.tenantName} · ${w.branch.name}. Existencias por unidad base.`} action={<Link className="button primary" to={w.base + '/compras'}>Registrar recepción</Link>} />
    {!http && <Notice tone="warning">Inventario requiere conexión HTTPS real.</Notice>}
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar la operación de inventario" />}
    {write.pending && <Notice tone="warning">Resultado pendiente de conciliación operacional. No repitas el ajuste ni el mínimo, incluso tras recargar. Un saldo consultado no identifica un movimiento ni prueba que la escritura falló. La API no ofrece consulta de resultado de ajuste ni GET del mínimo; conserva esta dirección y concilia antes de otra escritura.</Notice>}
    {write.pending && !adjustmentOpen && !minimumOpen && (lot || product) && <Button data-recovery-focus variant="secondary" disabled={write.busy} onClick={() => { if (lot) setAdjustmentOpen(true); else setMinimumOpen(true) }}>Revisar solicitud pendiente sin reenviar</Button>}
    {write.result && <Panel><div className="operation-body" role="status">{'stockMovementId' in write.result ? <><h2>Ajuste registrado por el servidor</h2><p>Antes: {decimalLabel(write.result.before)} · Después: {decimalLabel(write.result.after)}. Se reconsultan saldos y lotes; el resultado no se suma localmente.</p></> : <><h2>Mínimo guardado por el servidor</h2><p>Valor: {decimalLabel(write.result.minimumStockBase)} unidades base · {write.result.updatedAt}.</p></>}</div></Panel>}
    {!current.isError && !current.isFetching && current.data && <MetricGrid compact metrics={[
      ['Filas de esta página', String(current.data.length)],
      [mode === 'stock' ? 'Saldos en cero · página' : 'Lotes en cero · página', String(current.data.filter(row => compare(row.quantityAvailableBase, zero) === 0).length)],
      ...(mode === 'lots' && lots.data ? [['Fecha vencida · página', String(lots.data.filter(row => row.expirationDate && row.expirationDate < today).length)]] as [string, string][] : []),
    ]} />}
    <PageToolbar actions={<><Button variant="secondary" disabled={write.busy || write.pending} onClick={() => setFilterOpen(true)}>Filtrar por producto</Button>{w.access.role === 'owner' && product && <Button variant="secondary" disabled={write.locked} onClick={() => { setDirty(false); setMinimumOpen(true) }}>Configurar mínimo</Button>}</>}>
      <p>{product ? `Producto: ${product.product.name}` : 'Todos los productos de esta sucursal'}</p>
      {product && <Button variant="quiet" disabled={write.busy || write.pending} onClick={() => { setProduct(null); setOffset(0); setLot(null) }}>Quitar filtro</Button>}
    </PageToolbar>
    <div className="operation-actions"><Badge tone="info">Sucursal: {w.branch.name}</Badge><Badge tone="neutral">Referencia de fechas: {today} · Lima</Badge></div>
    <details className="inline-help"><summary>Cómo interpretar stock, mínimos y movimientos</summary><div>Los indicadores cuentan solo esta página. Un saldo cero no equivale al riesgo por mínimo configurado. El contrato de Inventario no ofrece un directorio de movimientos; no se simula un historial. {w.access.role === 'owner' && <Link className="text-link" to={w.base + '/reportes'}>Consultar stock crítico y vencimientos en Reportes</Link>}</div></details>
    <Modal open={filterOpen} title="Filtrar inventario" onClose={() => setFilterOpen(false)}><div className="operation-body"><ProductLookup onSelect={value => { setProduct(value); setOffset(0); setLot(null); setFilterOpen(false) }} /></div></Modal>
    <Panel><div className="operation-body"><nav className="task-tabs" aria-label="Consulta de inventario"><Button variant="quiet" aria-pressed={mode === 'stock'} onClick={() => { setMode('stock'); setOffset(0); if (!write.pending) setLot(null) }}>Existencias</Button><Button variant="quiet" aria-pressed={mode === 'lots'} onClick={() => { setMode('lots'); setOffset(0); if (!write.pending) setLot(null) }}>Lotes</Button><Button variant="quiet" aria-pressed={mode === 'expiring'} onClick={() => { setMode('expiring'); setOffset(0); if (!write.pending) setLot(null) }}>Próximos a vencer (30 días)</Button></nav>
      {current.isFetching && <p role="status">Consultando inventario…</p>}
      {current.isError && <ApiErrorState error={current.error} title="No pudimos consultar inventario" />}
      {mode === 'stock' && stock.data && !stock.isError && <div className="operation-table"><table><caption>Existencias disponibles en unidades base</caption><thead><tr><th>Producto</th><th>Cantidad disponible</th></tr></thead><tbody>{stock.data.map(row => <tr key={row.businessProductId}><td><ProductLabel id={row.businessProductId} /></td><td>{decimalLabel(row.quantityAvailableBase)}</td></tr>)}</tbody></table>{!stock.data.length && <p>No hay saldos registrados en esta página para el filtro seleccionado. No es un error de consulta.</p>}</div>}
      {mode === 'lots' && lots.data && !lots.isError && <div className="operation-table"><table><caption>Lotes de la sucursal</caption><thead><tr><th>Producto</th><th>Lote / vencimiento</th><th>Cantidad disponible</th><th>Acción</th></tr></thead><tbody>{lots.data.map(row => <tr key={row.inventoryLotId}><td><ProductLabel id={row.businessProductId} /></td><td>{row.batchNumber ?? 'Sin número de lote'} / <LotExpiry value={row.expirationDate} today={today} /></td><td>{decimalLabel(row.quantityAvailableBase)}</td><td><Button variant="quiet" disabled={write.locked} onClick={() => { setDirty(false); setLot(row); setAdjustmentOpen(true) }}>Ajustar lote {row.batchNumber ?? 'sin número'}</Button></td></tr>)}</tbody></table>{!lots.data.length && <p>No hay lotes en esta página.</p>}</div>}
      {mode === 'expiring' && expiring.data && !expiring.isError && <div className="operation-table"><p>Ventana del backend: hoy a 30 días, calendario de Lima, saldo positivo. Esta respuesta no incluye número de lote; consúltalo en Lotes.</p><table><caption>Lotes próximos a vencer</caption><thead><tr><th>Producto</th><th>Vencimiento</th><th>Cantidad disponible</th>{w.access.role === 'owner' && <th>Valor de costo</th>}<th>Valor potencial de venta</th></tr></thead><tbody>{expiring.data.map(row => <tr key={row.inventoryLotId}><td>{row.productName}</td><td>{row.expirationDate}</td><td>{decimalLabel(row.quantityAvailableBase)}</td>{w.access.role === 'owner' && <td>{row.costValue === null ? 'No disponible' : `S/ ${decimalLabel(row.costValue, 2)}`}</td>}<td>S/ {decimalLabel(row.potentialSaleValue, 2)}</td></tr>)}</tbody></table>{!expiring.data.length && <p>No hay lotes próximos a vencer en esta página.</p>}</div>}
      <PageControls offset={offset} count={current.isError ? undefined : current.data?.length} busy={current.isFetching} change={value => { setOffset(value); if (!write.pending) setLot(null) }} refresh={() => { if (!write.pending) setLot(null); void current.refetch() }} />
    </div></Panel>
    <Modal open={adjustmentOpen && lot !== null} title="Ajustar lote" onClose={() => { setAdjustmentOpen(false); setLot(null); setDirty(false) }} onRecoveryClose={() => setAdjustmentOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="operation-body" onChangeCapture={() => setDirty(true)}>{write.error != null && <ApiErrorState error={write.error} />}
        {write.pending && <Notice tone="warning">Resultado pendiente. Conserva esta dirección y concilia el movimiento antes de otra escritura; consultar el saldo no identifica el ajuste.</Notice>}
        {lot && <AdjustmentForm key={lot.inventoryLotId + ':' + decimalLabel(lot.quantityAvailableBase)} lot={lot} write={write} onSaved={() => { setAdjustmentOpen(false); setLot(null); setDirty(false) }} />}
      </div>
    </Modal>
    <Modal open={minimumOpen && Boolean(product)} title="Configurar mínimo de stock" onClose={() => { setMinimumOpen(false); setDirty(false) }} onRecoveryClose={() => setMinimumOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="operation-body" onChangeCapture={() => setDirty(true)}>{write.error != null && <ApiErrorState error={write.error} />}
        {write.pending && <Notice tone="warning">Resultado pendiente. La API no permite consultar el mínimo actual; no lo vuelvas a guardar sin conciliación.</Notice>}
        {w.access.role === 'owner' && product && <MinimumForm key={product.product.id} productId={product.product.id} write={write} onSaved={() => { setMinimumOpen(false); setDirty(false) }} />}
        {write.result && !('stockMovementId' in write.result) && <p role="status">Mínimo guardado: {decimalLabel(write.result.minimumStockBase)} unidades base.</p>}
      </div>
    </Modal>
  </div>
}
function LotExpiry({ value, today }: { value: string | null; today: string }) {
  if (!value) return <span>No informado</span>
  const nextMonth = new Date(`${today}T00:00:00Z`); nextMonth.setUTCDate(nextMonth.getUTCDate() + 30)
  const state = value < today ? 'Fecha vencida' : value === today ? 'Vence hoy' : value <= nextMonth.toISOString().slice(0, 10) ? 'Próximos 30 días' : 'Posterior'
  return <span className="lot-expiry"><DateText value={value} dateOnly /><Badge tone={state === 'Posterior' ? 'neutral' : 'warning'}>{state}</Badge></span>
}
