import { EditLocalProduct } from './EditLocalProduct'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Package, Pill, LayoutGrid, List, Plus } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { SearchField, TextField } from '../../shared/ui/TextField'
import { Drawer, Modal } from '../../shared/ui/Modal'
import { PageToolbar } from '../../shared/ui/PageToolbar'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/States'
import { catalogApi } from './api'
import type { Category, GlobalProduct, ProductDetail } from './api'
import { useCatalogWrite } from './useCatalogWrite'
import { CreateProductForm } from './CreateProductForm'
import { MedicineView } from './ProductFields'
import { EditPrices, EditStatus, ProductDetailPanel, UnitEditor } from './ProductDetailPanel'
import type { ProductTask } from './ProductDetailPanel'
import './catalog.css'

export function ProductsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Productos requiere acceso de propietario</h1><p>El servidor conserva la autorización de cada operación.</p></Panel>
  return <CatalogWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CatalogWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime()
  const [offset, setOffset] = useState(0); const [globalOffset, setGlobalOffset] = useState(0)
  const [mode, setMode] = useState<'products' | 'global'>('products')
  const [createOpen, setCreateOpen] = useState(false); const [detailOpen, setDetailOpen] = useState(false)
  const [task, setTask] = useState<ProductTask | null>(null)
  const [taskOpen, setTaskOpen] = useState(false)
  const [dirty, setDirty] = useState(false); const [formEpoch, setFormEpoch] = useState(0)
  const [editSnapshot, setEditSnapshot] = useState<ProductDetail | null>(null); const [editEpoch, setEditEpoch] = useState(0)
  const [filter, setFilter] = useState(''); const [status, setStatus] = useState('all')
  const [categoryFilter, setCategoryFilter] = useState(''); const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid')
  const [selected, setSelected] = useState(''); const [global, setGlobal] = useState<GlobalProduct | null>(null)
  const [reviewed, setReviewed] = useState(false)
  const [recoveryCode, setRecoveryCode] = useState('')
  const write = useCatalogWrite(id => { setSelected(id) }, verified => { setEditSnapshot(verified); setDirty(false); setEditEpoch(value => value + 1) })
  const key = (resource: string, ...values: unknown[]) => scopedKey(w, resource, w.session.userId, w.access.membershipId, ...values)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http) }
  const products = useQuery({ queryKey: key('catalog-products', offset), queryFn: ({ signal }) => api().list(w, offset, 50, signal), enabled: Boolean(http), retry: false, staleTime: 0 })
  const categories = useQuery({ queryKey: key('catalog-categories'), queryFn: async ({ signal }) => {
    const result: Category[] = []
    for (let page = 0; page <= 10000; page += 100) {
      const rows = await api().categories(w, page, 100, signal); result.push(...rows)
      if (rows.length < 100) return result
    }
    throw new ApiError(0, 'catalog.category_page_limit')
  }, enabled: Boolean(http), retry: false, staleTime: 0 })
  const globals = useQuery({ queryKey: key('catalog-global', globalOffset), queryFn: ({ signal }) => api().global(w, globalOffset, 50, signal), enabled: Boolean(http && mode === 'global'), retry: false, staleTime: 0 })
  const detail = useQuery({ queryKey: key('catalog-detail', selected), queryFn: ({ signal }) => api().detail(w, selected, signal), enabled: Boolean(http && selected), retry: false, staleTime: 0 })
  const categoryName = (id: string) => categories.data?.find(row => row.id === id)?.name ?? 'No disponible en las categorías consultables'
  const visibleProducts = products.data?.filter(item => (!categoryFilter || item.categoryId === categoryFilter) && (!filter.trim() || [item.name, item.internalCode, item.brandOrLaboratory, ...(item.medicine?.activeIngredients ?? [])].some(value => value.toLocaleLowerCase('es-PE').includes(filter.trim().toLocaleLowerCase('es-PE')))) && (status === 'all' || item.isActive === (status === 'active')))
  function openDetail(id: string) { setReviewed(false); setSelected(id); setDetailOpen(true) }
  function closeCreate() { setCreateOpen(false); setDirty(false); setGlobal(null); setFormEpoch(value => value + 1) }
  function openCreate(reference: GlobalProduct | null) { setTask(null); setTaskOpen(false); setGlobal(reference); setDirty(false); setFormEpoch(value => value + 1); setCreateOpen(true) }
  function edit(value: ProductTask) { setDetailOpen(false); setDirty(false); setEditSnapshot(detail.data ?? null); setEditEpoch(epoch => epoch + 1); setTask(value); setTaskOpen(true) }
  const taskDetail = editSnapshot ?? detail.data
  const recovery = write.pending && <Panel><div className="catalog-body"><h2>Operación pendiente de conciliación</h2><p>No reenvíes la solicitud. Conserva el código original; una consulta sin cambios no demuestra que la escritura falló.</p>
    <Button variant="secondary" disabled={write.busy} onClick={() => { setReviewed(false); void write.consult(selected || undefined) }}>Consultar producto de la operación</Button>
    {write.observed && <><p>Producto consultado: {write.observed.product.name} · Código {write.observed.product.internalCode}. Revisa sus atributos contra la solicitud original.</p>
      <p>{write.observed.product.brandOrLaboratory} · {categoryName(write.observed.product.categoryId)} · {write.observed.product.isActive ? 'Activo' : 'Inactivo'} · Barras: {write.observed.product.barcode ?? 'No indicado'}</p>
      <p>Minorista base: S/ {decimalLabel(write.observed.product.retailPrice, 2)} · Mayorista: {write.observed.product.wholesalePrice ? `S/ ${decimalLabel(write.observed.product.wholesalePrice, 2)}` : 'Sin configurar'}</p>
      <MedicineView value={write.observed.product.medicine} />
      <ul>{write.observed.units.map(unit => <li key={unit.id}>{unit.name} · factor {decimalLabel(unit.conversionToBase)} · {unit.isBaseUnit ? 'Base' : 'Presentación'} · {unit.isActive ? 'Activa' : 'Inactiva'}</li>)}</ul>
      <TextField label="Código interno de la solicitud original" value={recoveryCode} maxLength={64} onChange={event => { setRecoveryCode(event.target.value); setReviewed(false) }} />
      <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué el código y los atributos; la operación quedó conciliada, sin otra solicitud en curso.</label>
      <Button variant="secondary" disabled={!reviewed || write.busy || !write.canAcknowledge || recoveryCode.trim() !== write.observed.product.internalCode} onClick={() => { write.acknowledge(recoveryCode); setReviewed(false); setRecoveryCode('') }}>Continuar tras conciliación verificada</Button></>}
  </div></Panel>
  return <div className="catalog-page">
    <PageHeader icon={Package} eyebrow="Catálogo · mantenimiento" title="Productos" description={`Catálogo de ${w.tenantName}. Consulta existencias y lotes en Inventario.`} action={<Button disabled={write.locked} onClick={() => openCreate(null)}><Plus size={18} aria-hidden="true" />Nuevo producto</Button>} />
    {!http && <Notice tone="warning">Esta pantalla requiere conexión HTTPS real con MediPOS.</Notice>}
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar la operación de catálogo" />}
    {write.notice && <p role="status">{write.notice}</p>}
    {!createOpen && !taskOpen && recovery}
    {write.pending && !createOpen && !taskOpen && <Button data-recovery-focus variant="secondary" disabled={write.busy} onClick={() => { if (task) setTaskOpen(true); else setCreateOpen(true) }}>Revisar solicitud pendiente sin reenviar</Button>}
    <PageToolbar actions={<Link className="text-link" to={w.base + '/importacion'}>Importar desde Excel</Link>}>
      <nav aria-label="Catálogo" className="task-tabs"><Button variant="quiet" aria-pressed={mode === 'products'} onClick={() => setMode('products')}>Productos de la botica</Button><Button variant="quiet" aria-pressed={mode === 'global'} onClick={() => setMode('global')}>Referencias del catálogo global</Button></nav>
    </PageToolbar>
    {categories.isError && <ApiErrorState error={categories.error} title="No pudimos consultar las categorías" />}
    {categories.isError && <Button variant="quiet" onClick={() => void categories.refetch()}>Consultar categorías</Button>}
    <Modal open={createOpen} wide title={global ? 'Añadir referencia a mi botica' : 'Nuevo producto'} description="Registra sus datos, revisa los precios y configura las presentaciones después de guardar." onClose={closeCreate} onRecoveryClose={() => setCreateOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="catalog-body">{write.error != null && <ApiErrorState error={write.error} />}{recovery}
        {categories.isPending ? <p>Consultando categorías…</p> : categories.data && !categories.isError && <CreateProductForm key={`${global?.id ?? 'local'}/${formEpoch}`} categories={categories.data} global={global} write={write} onDirty={setDirty} />}
        {categories.isError && <ApiErrorState error={categories.error} retry={() => void categories.refetch()} />}
        {write.notice && selected && <Button disabled={write.locked} onClick={() => { closeCreate(); edit('units') }}>Configurar presentaciones del producto guardado</Button>}
      </div>
    </Modal>
    {mode === 'global' && <Panel><div className="catalog-body"><h2>Catálogo global disponible</h2><p>Referencia global, sin precios privados ni existencias físicas. Asociar crea un producto propio, no stock.</p>
      {globals.isPending && <p role="status">Consultando catálogo global…</p>}
      {globals.isError && <ApiErrorState error={globals.error} title="No pudimos consultar el catálogo global" />}
      {globals.data && !globals.isError && <>{!globals.data.length && <p>No hay productos globales en esta página.</p>}
        <ul>{globals.data.map(item => <li key={item.id}>{item.name} · {categoryName(item.categoryId)} · {item.brandOrLaboratory} <Button variant="quiet" disabled={write.locked} onClick={() => openCreate(item)}>Añadir {item.name} a mi botica</Button></li>)}</ul></>}
      <Pagination offset={globalOffset} change={setGlobalOffset} count={globals.data?.length} busy={globals.isFetching} refresh={() => void globals.refetch()} />
    </div></Panel>}
    {mode === 'products' && <Panel><div className="catalog-body"><h2>Productos de la botica</h2>
      <PageToolbar actions={<div className="segmented-control" role="group" aria-label="Vista del catálogo"><Button variant="quiet" aria-pressed={viewMode === 'grid'} onClick={() => setViewMode('grid')}><LayoutGrid size={18} aria-hidden="true" />Tarjetas</Button><Button variant="quiet" aria-pressed={viewMode === 'list'} onClick={() => setViewMode('list')}><List size={18} aria-hidden="true" />Lista</Button></div>}><SearchField label="Filtrar esta página" hint="Solo filtra los productos cargados en la página actual." placeholder="Nombre, código, marca o principio activo" value={filter} onChange={event => setFilter(event.target.value)} />
        <label>Estado en esta página<select aria-label="Estado en esta página" value={status} onChange={event => setStatus(event.target.value)}><option value="all">Todos</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select></label>
      </PageToolbar>
      <div className="category-chips" role="group" aria-label="Categoría del catálogo"><Button variant="quiet" aria-pressed={!categoryFilter} onClick={() => setCategoryFilter('')}>Todas las categorías</Button>{!categories.isError && categories.data?.filter(row => row.isActive).map(row => <Button key={row.id} variant="quiet" aria-pressed={categoryFilter === row.id} onClick={() => setCategoryFilter(row.id)}>{row.name}</Button>)}</div>
      <p className="data-caption">{visibleProducts?.length ?? '—'} productos visibles en esta página · precios por unidad base. Crear o asociar no genera existencias.</p>
      {products.isPending && <p role="status">Consultando productos…</p>}
      {products.isError && <ApiErrorState error={products.error} title="No pudimos consultar los productos" />}
      {products.data && !products.isError && <>{!visibleProducts?.length ? <EmptyState title="Sin productos para esta vista" description={products.data.length ? 'Cambia los filtros de esta página o consulta la siguiente.' : 'Crea un producto o añade una referencia del catálogo global. El stock se registra después, en Compras.'} /> : viewMode === 'grid' ? <ul className="catalog-card-grid">{visibleProducts.map(item => <li key={item.id} className="catalog-product-card">
        <div className="catalog-card-heading"><span className="module-icon">{item.productType === 'medicine' ? <Pill size={26} aria-hidden="true" /> : <Package size={26} aria-hidden="true" />}</span><Badge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Activo' : 'Inactivo'}</Badge></div>
        <small>{categoryName(item.categoryId)}</small><h3>{item.name}</h3><p>{item.brandOrLaboratory}</p><p className="data-caption">Código {item.internalCode} · {item.productType === 'medicine' ? 'Medicamento' : 'Venta general'}</p>
        {item.medicine && <details className="catalog-composition"><summary>Composición documentada</summary><p>{item.medicine.components.map(row => row.ingredient + ' ' + row.strengthNormalized).join(' · ')}</p></details>}
        <div className="catalog-card-price"><small>Minorista · unidad base</small><strong>S/ {decimalLabel(item.retailPrice, 2)}</strong><small>Mayorista: {item.wholesalePrice ? `S/ ${decimalLabel(item.wholesalePrice, 2)}` : 'Sin configurar'}</small></div>
        <Button variant="secondary" disabled={write.busy || (write.pending && task !== null && selected !== item.id)} onClick={() => openDetail(item.id)}>Ver detalle y presentaciones</Button>
      </li>)}</ul> : <div className="catalog-table"><table><caption>Precios por unidad base · presentaciones en el detalle</caption><thead><tr><th>Producto/código</th><th>Categoría/tipo</th><th>Marca o laboratorio</th><th>Precios base</th><th>Estado</th><th>Detalle</th></tr></thead><tbody>{visibleProducts.map(item => <tr key={item.id}>
        <td>{item.name}<br />{item.internalCode}</td><td>{categoryName(item.categoryId)}<br />{item.productType === 'medicine' ? 'Medicamento' : 'Venta general'}</td><td>{item.brandOrLaboratory}</td>
        <td>Minorista S/ {decimalLabel(item.retailPrice, 2)}<br />Mayorista {item.wholesalePrice ? `S/ ${decimalLabel(item.wholesalePrice, 2)}` : 'No configurado'}</td><td><Badge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Activo' : 'Inactivo'}</Badge></td>
        <td><Button variant="quiet" disabled={write.busy || (write.pending && task !== null && selected !== item.id)} onClick={() => openDetail(item.id)}>Ver {item.name}</Button></td>
      </tr>)}</tbody></table></div>}</>}
      <Pagination offset={offset} change={setOffset} count={products.data?.length} busy={products.isFetching} refresh={() => void products.refetch()} />
    </div></Panel>}
    <Drawer open={detailOpen} title="Detalle del producto" onClose={() => setDetailOpen(false)}>
      {detail.isPending && <p role="status">Consultando detalle…</p>}
      {detail.isError && <ApiErrorState error={detail.error} title="No pudimos consultar este producto" />}
      {detail.data && !detail.isError && <ProductDetailPanel key={detail.data.product.id} detail={detail.data} write={write} onEdit={edit} />}
      <Button variant="quiet" disabled={detail.isFetching} onClick={() => void detail.refetch()}>Actualizar detalle</Button>
    </Drawer>
    <Modal open={taskOpen && task !== null} wide={task === 'units'} title={task === 'metadata' ? 'Corregir datos locales' : task === 'prices' ? 'Editar precios' : task === 'units' ? 'Configurar presentaciones' : 'Cambiar estado del producto'} onClose={() => { setTaskOpen(false); setTask(null); setDirty(false) }} onRecoveryClose={() => setTaskOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="catalog-body">
        {write.error != null && <ApiErrorState error={write.error} />}{recovery}{write.notice && <p role="status">{write.notice}</p>}
        {taskDetail && <>
          {task === 'metadata' && <EditLocalProduct key={editEpoch} detail={taskDetail} categories={categories.data ?? []} write={write} onDirty={setDirty} />}
          {task === 'prices' && <EditPrices key={editEpoch} detail={taskDetail} write={write} onDirty={setDirty} />}
          {task === 'status' && <EditStatus key={editEpoch} detail={taskDetail} write={write} onDirty={setDirty} />}
          {task === 'units' && <UnitEditor key={editEpoch} detail={taskDetail} write={write} onDirty={setDirty} />}
        </>}
      </div>
    </Modal>
  </div>
}
function Pagination({ offset, change, count, busy, refresh }: { offset: number; change: (value: number) => void; count?: number; busy: boolean; refresh: () => void }) {
  return <div className="catalog-actions"><Button variant="quiet" disabled={busy} onClick={refresh}>Actualizar lista</Button>
    <Button variant="quiet" disabled={busy || offset === 0} onClick={() => change(offset - 50)}>Página anterior</Button><span>Página {offset / 50 + 1}</span>
    <Button variant="quiet" disabled={busy || count !== 50 || offset >= 10000} onClick={() => change(offset + 50)}>Página siguiente</Button></div>
}
