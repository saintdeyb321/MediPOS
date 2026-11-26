import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Boxes, X } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { InlineProductLookup, ProductLabel } from '../catalog'
import type { ProductChoice } from '../catalog'
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
import { HelpPopover } from '../../shared/ui/HelpPopover'
import { DataSection } from '../../shared/ui/DataSection'
import { EmptyState, LoadingState } from '../../shared/ui/States'
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
  const [product, setProduct] = useState<ProductChoice | null>(null); const [lot, setLot] = useState<LotStock | null>(null)
  const [minimumOpen, setMinimumOpen] = useState(false)
  const [adjustmentOpen, setAdjustmentOpen] = useState(false)
  const [minimumEpoch, setMinimumEpoch] = useState(0)
  const [dirty, setDirty] = useState(false)
  const key = (resource: string) => scopedKey(w, resource, w.session.userId, w.access.membershipId, product?.id, offset)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return inventoryApi(http) }
  const stock = useQuery({ queryKey: key('stock'), queryFn: ({ signal }) => api().stock(w, product?.id, offset, 20, signal), enabled: Boolean(http && mode === 'stock'), retry: false, staleTime: 0, refetchOnWindowFocus: true })
  const lots = useQuery({ queryKey: key('inventory-lots'), queryFn: ({ signal }) => api().lots(w, product?.id, offset, 20, signal), enabled: Boolean(http && mode === 'lots'), retry: false, staleTime: 0, refetchOnWindowFocus: true })
  const expiring = useQuery({ queryKey: key('inventory-expiring'), queryFn: ({ signal }) => api().expiring(w, product?.id, offset, 20, signal), enabled: Boolean(http && mode === 'expiring'), retry: false, staleTime: 0, refetchOnWindowFocus: true })
  const current = mode === 'stock' ? stock : mode === 'lots' ? lots : expiring
  const dateParts = new Intl.DateTimeFormat('en', { timeZone: 'America/Lima', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
  const today = ['year', 'month', 'day'].map(type => dateParts.find(part => part.type === type)!.value).join('-')
  function filterProduct(value: ProductChoice | null) {
    if (write.busy || write.pending) return
    setProduct(value); setOffset(0); setLot(null)
  }
  const reception = w.base + '/compras?nueva=1&origen=inventario'
  return <div className="operations"><PageHeader icon={Boxes} eyebrow="" title="Inventario" action={<Link className="button primary" to={reception}>Registrar recepción</Link>} />
    {!http && <Notice tone="warning">Inventario requiere conexión HTTPS real.</Notice>}
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar la operación de inventario" />}
    {write.pending && !write.recoverable && <p role="alert">Solicitud anterior sin constancia consultable. Conserva su referencia y concilia con soporte antes de otra escritura.</p>}
    {write.pending && <Button disabled={write.busy || !write.recoverable} onClick={() => void write.reconcile()}>Consultar constancia de la operación original</Button>}
    {write.pending && <Notice tone="warning">Resultado pendiente de conciliación operacional. No repitas el ajuste ni el mínimo, incluso tras recargar. Un saldo consultado no identifica un movimiento ni prueba que la escritura falló. Conserva esta dirección y consulta la constancia original antes de otra escritura.</Notice>}
    {write.pending && !adjustmentOpen && !minimumOpen && (lot || product) && <Button data-recovery-focus variant="secondary" disabled={write.busy} onClick={() => { if (lot) setAdjustmentOpen(true); else setMinimumOpen(true) }}>Revisar solicitud pendiente sin reenviar</Button>}
    {write.result && <Panel><div className="operation-body" role="status">{'stockMovementId' in write.result ? <><h2>Ajuste registrado por el servidor</h2><p>Antes: {decimalLabel(write.result.before)} · Después: {decimalLabel(write.result.after)}. Se reconsultan saldos y lotes; el resultado no se suma localmente.</p></> : <><h2>Mínimo guardado por el servidor</h2><p>Valor: {decimalLabel(write.result.minimumStockBase)} unidades base · {write.result.updatedAt}.</p></>}</div></Panel>}
    <DataSection className="inventory-data" tabs={<div className="data-view-heading">
        <nav className="task-tabs" aria-label="Consulta de inventario">
          {(['stock', 'lots', 'expiring'] as const).map(view => <Button key={view} variant="quiet" aria-pressed={mode === view} aria-label={view === 'expiring' ? 'Próximos a vencer (30 días)' : undefined} onClick={() => {
            setMode(view); setOffset(0); if (!write.pending) setLot(null)
          }}>{view === 'stock' ? 'Existencias' : view === 'lots' ? 'Lotes' : 'Por vencer'}</Button>)}
        </nav>
        <HelpPopover label="Sobre el stock"><p>Disponible se expresa en la unidad base del producto. Por vencer muestra los próximos 30 días en Lima. Puedes filtrar un producto inactivo al elegirlo en Existencias o Lotes.</p></HelpPopover>
      </div>}
      toolbar={<PageToolbar actions={w.access.role === 'owner' && product && <Button variant="secondary" disabled={write.locked} onClick={() => { setDirty(false); setMinimumOpen(true) }}>Configurar mínimo</Button>}>
        <InlineProductLookup onSelect={filterProduct} disabled={write.busy || write.pending} />
        {product && <Button variant="quiet" disabled={write.busy || write.pending} aria-label={`Quitar filtro ${product.name}`}
          onClick={() => filterProduct(null)}>{product.name}<X size={16} aria-hidden="true" /></Button>}
      </PageToolbar>}
      footer={<>
        {mode === 'stock' && !current.isError && !current.isFetching && Boolean(stock.data?.length) && <dl className="inventory-page-totals">
          <div><dt>Productos en esta página</dt><dd>{stock.data!.length}</dd></div>
          <div><dt>Sin stock en esta página</dt><dd>{stock.data!.filter(row => compare(row.quantityAvailableBase, zero) === 0).length}</dd></div>
        </dl>}
        <PageControls offset={offset} count={current.isError ? undefined : current.data?.length} busy={current.isFetching} change={value => { setOffset(value); if (!write.pending) setLot(null) }} />
      </>}>
      {!current.data && current.isFetching && <LoadingState label="Consultando inventario…" />}
      {current.isError && <ApiErrorState error={current.error} title="No pudimos consultar inventario" retry={() => void current.refetch()} />}
      {current.data?.length === 0 && !current.isError && <EmptyState
        title={mode === 'stock' ? product ? 'No encontramos existencias para este producto' : 'Aún no hay existencias'
          : mode === 'lots' ? 'No hay lotes en esta página' : 'No hay lotes próximos a vencer'}
        action={product ? <Button variant="secondary" disabled={write.pending || write.busy} onClick={() => filterProduct(null)}>Limpiar filtro</Button>
          : mode === 'stock' ? <Link className="button secondary" to={reception}>Ir a Compras</Link> : undefined}
        description={product ? 'Quita el filtro para buscar en toda la sucursal.'
          : mode === 'stock' ? 'Registra una recepción de compra para incorporar los primeros productos.'
          : mode === 'lots' ? 'Los lotes aparecerán al registrar una recepción con existencias.' : 'Aquí aparecen lotes con saldo positivo que vencen en los próximos 30 días, según la hora de Lima.'} />}
      {mode === 'stock' && Boolean(stock.data?.length) && !stock.isError && <div className="operation-table" role="region" aria-label="Tabla de existencias" tabIndex={0}>
        <table><thead><tr><th scope="col">Producto</th><th scope="col" className="table-number">Disponible</th></tr></thead>
          <tbody>{stock.data?.map(row => <tr key={row.businessProductId}><td><ProductLabel id={row.businessProductId} onSelect={!product && !write.busy && !write.pending ? filterProduct : undefined} /></td><td className="table-number">{decimalLabel(row.quantityAvailableBase)}</td></tr>)}</tbody></table>
      </div>}
      {mode === 'lots' && Boolean(lots.data?.length) && !lots.isError && <div className="operation-table" role="region" aria-label="Tabla de lotes" tabIndex={0}>
        <table><thead><tr><th scope="col">Producto</th><th scope="col">Lote y vencimiento</th><th scope="col" className="table-number">Disponible (base)</th><th scope="col" className="table-action">Acción</th></tr></thead>
          <tbody>{lots.data?.map(row => <tr key={row.inventoryLotId}><td><ProductLabel id={row.businessProductId} onSelect={!product && !write.busy && !write.pending ? filterProduct : undefined} /></td>
            <td><strong>{row.batchNumber ?? 'Sin número de lote'}</strong><LotExpiry value={row.expirationDate} today={today} /></td>
            <td className="table-number">{decimalLabel(row.quantityAvailableBase)}</td>
            <td className="table-action"><Button variant="quiet" disabled={write.locked} onClick={() => { setDirty(false); setLot(row); setAdjustmentOpen(true) }}>Ajustar lote {row.batchNumber ?? 'sin número'}</Button></td>
          </tr>)}</tbody></table>
      </div>}
      {mode === 'expiring' && Boolean(expiring.data?.length) && !expiring.isError && <>
        <p className="data-caption">Lotes con saldo positivo. Consulta su número en la pestaña Lotes.</p>
        <div className="operation-table" role="region" aria-label="Tabla de vencimientos" tabIndex={0}>
          <table><caption>Lotes próximos a vencer · 30 días (Lima)</caption><thead><tr><th scope="col">Producto</th><th scope="col">Vencimiento</th><th scope="col" className="table-number">Disponible (base)</th>{w.access.role === 'owner' && <th scope="col" className="table-number">Valor de costo</th>}<th scope="col" className="table-number">Valor potencial de venta</th></tr></thead>
            <tbody>{expiring.data?.map(row => <tr key={row.inventoryLotId}><td>{row.productName}</td><td><DateText value={row.expirationDate} dateOnly /></td><td className="table-number">{decimalLabel(row.quantityAvailableBase)}</td>
              {w.access.role === 'owner' && <td className="table-number">{row.costValue === null ? 'No disponible' : `S/ ${decimalLabel(row.costValue, 2)}`}</td>}
              <td className="table-number">S/ {decimalLabel(row.potentialSaleValue, 2)}</td></tr>)}</tbody></table>
        </div>
      </>}
    </DataSection>
    <Modal open={adjustmentOpen && lot !== null} title="Ajustar lote" onClose={() => { setAdjustmentOpen(false); setLot(null); setDirty(false) }} onRecoveryClose={() => setAdjustmentOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="operation-body">{write.error != null && <ApiErrorState error={write.error} />}
        {write.pending && <Notice tone="warning">Resultado pendiente. Conserva esta dirección y concilia el movimiento antes de otra escritura; consultar el saldo no identifica el ajuste.</Notice>}
        {lot && <AdjustmentForm key={lot.inventoryLotId + ':' + decimalLabel(lot.quantityAvailableBase)} lot={lot} write={write} onDirty={setDirty} onSaved={() => { setAdjustmentOpen(false); setLot(null); setDirty(false) }} />}
      </div>
    </Modal>
    <Modal open={minimumOpen && Boolean(product)} title="Configurar mínimo de stock" onClose={() => { setMinimumOpen(false); setDirty(false); setMinimumEpoch(value => value + 1) }} onRecoveryClose={() => setMinimumOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="operation-body">{write.error != null && <ApiErrorState error={write.error} />}
        {write.pending && <Notice tone="warning">Resultado pendiente. La API no permite consultar el mínimo actual; no lo vuelvas a guardar sin conciliación.</Notice>}
        {w.access.role === 'owner' && product && <MinimumForm key={`${product.id}/${minimumEpoch}`} productId={product.id} write={write} onDirty={setDirty} onSaved={() => { setMinimumOpen(false); setDirty(false) }} />}
        {write.result && !('stockMovementId' in write.result) && <p role="status">Mínimo guardado: {decimalLabel(write.result.minimumStockBase)} unidades base.</p>}
      </div>
    </Modal>
  </div>
}
function LotExpiry({ value, today }: { value: string | null; today: string }) {
  if (!value) return <span>No informado</span>
  const nextMonth = new Date(`${today}T00:00:00Z`); nextMonth.setUTCDate(nextMonth.getUTCDate() + 30)
  const state = value < today ? 'Fecha vencida' : value === today ? 'Vence hoy' : value <= nextMonth.toISOString().slice(0, 10) ? 'Próximos 30 días' : 'Posterior'
  return <span className="lot-expiry"><DateText value={value} dateOnly /><Badge tone={state === 'Fecha vencida' ? 'error' : state === 'Posterior' ? 'neutral' : 'warning'}>{state}</Badge></span>
}
