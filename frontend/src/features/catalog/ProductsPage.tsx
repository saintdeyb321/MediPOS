import { EditLocalProduct } from './EditLocalProduct'
import { useState } from 'react'
import { PageControls } from '../../shared/ui/PageControls'
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
import '../../shared/styles/operations.css'
import { ActionMenu } from '../../shared/ui/ActionMenu'
import { DataSection } from '../../shared/ui/DataSection'
import { PageToolbar } from '../../shared/ui/PageToolbar'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/States'
import { catalogApi } from './api'
import type { CatalogProductFilters, Category, GlobalProduct, ProductDetail } from './api'
import { useCatalogWrite } from './useCatalogWrite'
import { CreateProductForm } from './CreateProductForm'
import { MedicineView } from './ProductFields'
import { EditPrices, EditStatus, ProductDetailPanel, UnitEditor } from './ProductDetailPanel'
import type { ProductTask } from './ProductDetailPanel'
import './catalog.css'

const emptyFilters: CatalogProductFilters = { text: '', categoryId: '', status: 'all' }

export function ProductsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Productos requiere acceso de propietario</h1><p>El servidor conserva la autorización de cada operación.</p></Panel>
  return <CatalogWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CatalogWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime()
  const [offset, setOffset] = useState(0); const [globalOffset, setGlobalOffset] = useState(0)
  const [mode, setMode] = useState<'products' | 'global' | 'categories'>('products')
  const [createOpen, setCreateOpen] = useState(false); const [detailOpen, setDetailOpen] = useState(false)
  const [task, setTask] = useState<ProductTask | null>(null)
  const [taskOpen, setTaskOpen] = useState(false)
  const [dirty, setDirty] = useState(false); const [formEpoch, setFormEpoch] = useState(0)
  const [editSnapshot, setEditSnapshot] = useState<ProductDetail | null>(null); const [editEpoch, setEditEpoch] = useState(0)
  const [filterInput, setFilterInput] = useState(emptyFilters)
  const [filters, setFilters] = useState(emptyFilters)
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('list')
  const [selected, setSelected] = useState(''); const [global, setGlobal] = useState<GlobalProduct | null>(null)
  const [reviewed, setReviewed] = useState(false)
  const [recoveryCode, setRecoveryCode] = useState('')
  const write = useCatalogWrite(id => { setSelected(id) }, verified => { setEditSnapshot(verified); setDirty(false); setEditEpoch(value => value + 1) })
  const key = (resource: string, ...values: unknown[]) => scopedKey(w, resource, w.session.userId, w.access.membershipId, ...values)
  const api = () => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http) }
  const products = useQuery({ queryKey: key('catalog-products', offset, filters), queryFn: ({ signal }) => api().list(w, offset, 50, signal, filters), enabled: Boolean(http && mode === 'products'), retry: false, staleTime: 0 })
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
  const visibleProducts = products.data
  const filtered = Boolean(filters.text || filters.categoryId || filters.status !== 'all')
  const editedFilters = filterInput.text.trim() !== filters.text || filterInput.categoryId !== filters.categoryId || filterInput.status !== filters.status
  function clearFilters() { setFilterInput(emptyFilters); setFilters(emptyFilters); setOffset(0) }
  function openDetail(id: string) { setReviewed(false); setSelected(id); setDetailOpen(true) }
  function closeCreate() { setCreateOpen(false); setDirty(false); setGlobal(null); setFormEpoch(value => value + 1) }
  function openCreate(reference: GlobalProduct | null) { setTask(null); setTaskOpen(false); setGlobal(reference); setDirty(false); setFormEpoch(value => value + 1); setCreateOpen(true) }
  function edit(value: ProductTask) { setDetailOpen(false); setDirty(false); setEditSnapshot(detail.data ?? null); setEditEpoch(epoch => epoch + 1); setTask(value); setTaskOpen(true) }
  const taskDetail = editSnapshot ?? detail.data
  const recoverySurface = createOpen ? 'create' : taskOpen && task !== null ? 'task' : 'page'
  const recovery = write.pending && <Panel><div className="catalog-body"><h2>Operación pendiente de conciliación</h2><p>No reenvíes la solicitud. Conserva el código original; una consulta sin cambios no demuestra que la escritura falló.</p>
    {!write.originalIdentified && <Notice tone="warning">Esta solicitud anterior no conservó su identificador original. Solicita a soporte que la verifique antes de continuar.</Notice>}
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
    <PageHeader icon={Package} eyebrow="" title="Productos" />
    {!http && <Notice tone="warning">Esta pantalla requiere conexión HTTPS real con MediPOS.</Notice>}
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar la operación de catálogo" />}
    {write.notice && <p role="status">{write.notice}</p>}
    {recoverySurface === 'page' && recovery}
    {write.pending && recoverySurface === 'page' && <Button data-recovery-focus variant="secondary" disabled={write.busy} onClick={() => { if (task) setTaskOpen(true); else setCreateOpen(true) }}>Revisar solicitud pendiente sin reenviar</Button>}
    {categories.isError && <ApiErrorState error={categories.error} title="No pudimos consultar las categorías" retry={() => void categories.refetch()} />}
    <Modal open={createOpen} wide title={global ? 'Añadir referencia a mi botica' : 'Nuevo producto'} description="Registra sus datos, revisa los precios y configura las presentaciones después de guardar." onClose={closeCreate} onRecoveryClose={() => setCreateOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="catalog-body">{write.error != null && <ApiErrorState error={write.error} />}{recoverySurface === 'create' && recovery}
        {categories.isPending ? <p>Consultando categorías…</p> : categories.data && !categories.isError && <CreateProductForm key={`${global?.id ?? 'local'}/${formEpoch}`} categories={categories.data} global={global} write={write} onDirty={setDirty} />}
        {categories.isError && <ApiErrorState error={categories.error} retry={() => void categories.refetch()} />}
        {write.notice && selected && <Button disabled={write.locked} onClick={() => { closeCreate(); edit('units') }}>Configurar presentaciones del producto guardado</Button>}
      </div>
    </Modal>
    <DataSection className="catalog-directory"
      tabs={<nav aria-label="Catálogo" className="task-tabs">
        <Button variant="quiet" aria-pressed={mode === 'products'} onClick={() => setMode('products')}>Mis productos</Button>
        <Button variant="quiet" aria-pressed={mode === 'global'} onClick={() => setMode('global')}>Catálogo global</Button>
        <Button variant="quiet" aria-pressed={mode === 'categories'} onClick={() => setMode('categories')}>Categorías</Button>
      </nav>}
      toolbar={mode === 'products' && <PageToolbar actions={<>
        <Link className="button secondary catalog-import-direct" to={w.base + '/importacion'}>Importar Excel</Link>
        <div className="catalog-import-menu"><ActionMenu label="Más"><Link className="button quiet" to={w.base + '/importacion'}>Importar Excel</Link></ActionMenu></div>
        <Button disabled={write.locked} onClick={() => openCreate(null)}><Plus size={18} aria-hidden="true" />Nuevo producto</Button>
      </>}>
        <form className="catalog-filters" aria-label="Filtros de mis productos" onSubmit={event => {
          event.preventDefault()
          if (editedFilters) { setFilters({ ...filterInput, text: filterInput.text.trim() }); setOffset(0) }
        }}>
          <SearchField label="Buscar en mis productos" placeholder="Nombre, código, marca o ingrediente" maxLength={128} value={filterInput.text} onChange={event => setFilterInput({ ...filterInput, text: event.target.value })} />
          <label>Categoría<select value={filterInput.categoryId} onChange={event => setFilterInput({ ...filterInput, categoryId: event.target.value })}><option value="">Todas</option>{!categories.isError && categories.data?.map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></label>
          <label>Estado<select value={filterInput.status} onChange={event => setFilterInput({ ...filterInput, status: event.target.value as CatalogProductFilters['status'] })}><option value="all">Todos</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select></label>
          <Button type="submit" variant="secondary" disabled={!http || !editedFilters}>Aplicar filtros</Button>
          {filtered && <Button variant="quiet" onClick={clearFilters}>Limpiar filtros</Button>}
        </form>
        <div className="segmented-control" role="group" aria-label="Vista del catálogo">
          <Button variant="quiet" aria-label="Lista" aria-pressed={viewMode === 'list'} onClick={() => setViewMode('list')}><List size={18} aria-hidden="true" /></Button>
          <Button variant="quiet" aria-label="Tarjetas" aria-pressed={viewMode === 'grid'} onClick={() => setViewMode('grid')}><LayoutGrid size={18} aria-hidden="true" /></Button>
        </div>
      </PageToolbar>}
      footer={mode === 'global'
        ? <PageControls size={50} offset={globalOffset} change={setGlobalOffset} count={globals.isError ? undefined : globals.data?.length} busy={globals.isFetching} />
        : mode === 'products' && <PageControls size={50} offset={offset} change={setOffset} count={products.isError ? undefined : products.data?.length} busy={products.isFetching} />}>
    {mode === 'categories' && <section className="catalog-body" aria-labelledby="catalog-categories-title">
      <h2 id="catalog-categories-title">Categorías globales</h2>
      {categories.isPending && <p role="status">Consultando categorías…</p>}
      {categories.isSuccess && categories.data.length === 0 && <p>No hay categorías disponibles.</p>}
      {categories.isSuccess && categories.data.length > 0 && <ul className="catalog-card-grid">
        {categories.data.map(category => <li key={category.id}><Panel>
          <div className="catalog-body"><h3>{category.name}</h3>
            <Button variant="secondary" onClick={() => {
              const next = { ...emptyFilters, categoryId: category.id }
              setFilterInput(next); setFilters(next); setOffset(0); setMode('products')
            }}>Ver productos de {category.name}</Button>
          </div>
        </Panel></li>)}
      </ul>}
    </section>}
    {mode === 'global' && <>
      {globals.isPending && <p role="status">Consultando catálogo global…</p>}
      {globals.isError && <ApiErrorState error={globals.error} title="No pudimos consultar el catálogo global" retry={() => void globals.refetch()} />}
      {globals.data && !globals.isError && <>{!globals.data.length && <p>No hay productos globales en esta página.</p>}
        {globals.data.length > 0 && <div className="catalog-table" role="region" aria-label="Productos globales" tabIndex={0}><table>
          <thead><tr><th scope="col">Producto</th><th scope="col">Categoría</th><th scope="col">Marca o laboratorio</th><th scope="col">Acción</th></tr></thead>
          <tbody>{globals.data.map(item => <tr key={item.id}><td>{item.name}</td><td>{categoryName(item.categoryId)}</td><td>{item.brandOrLaboratory}</td><td><Button variant="quiet" disabled={write.locked} onClick={() => openCreate(item)}>Añadir {item.name} a mi botica</Button></td></tr>)}</tbody>
        </table></div>}</>}
    </>}
    {mode === 'products' && <>
      {products.isPending && <p role="status">Consultando productos…</p>}
      {products.isError && <ApiErrorState error={products.error} title="No pudimos consultar los productos" retry={() => void products.refetch()} />}
      {visibleProducts && !products.isError && <>{!visibleProducts.length ? <EmptyState
        title={offset > 0 ? 'No hay productos en esta página' : filtered ? 'Sin coincidencias en mis productos' : 'Sin productos'}
        description={offset > 0 ? 'Vuelve a la página anterior.' : filtered ? 'Ajusta los filtros para buscar de nuevo.' : 'Crea un producto o añade una referencia del catálogo global.'}
        action={!filtered && offset === 0 && <Button variant="secondary" onClick={() => setMode('global')}>Explorar catálogo global</Button>} /> : viewMode === 'grid' ? <ul className="catalog-card-grid">{visibleProducts.map(item => <li key={item.id} className="catalog-product-card">
        <div className="catalog-card-heading"><span className="module-icon">{item.productType === 'medicine' ? <Pill size={26} aria-hidden="true" /> : <Package size={26} aria-hidden="true" />}</span><Badge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Activo' : 'Inactivo'}</Badge></div>
        <small>{categoryName(item.categoryId)}</small><h3>{item.name}</h3><p>{item.brandOrLaboratory}</p><p className="data-caption">Código {item.internalCode} · {item.productType === 'medicine' ? 'Medicamento' : 'Venta general'}</p>
        {item.medicine && <details className="catalog-composition"><summary>Composición documentada</summary><p>{item.medicine.components.map(row => row.ingredient + ' ' + row.strengthNormalized).join(' · ')}</p></details>}
        <div className="catalog-card-price"><small>Minorista · unidad base</small><strong>S/ {decimalLabel(item.retailPrice, 2)}</strong><small>Mayorista: {item.wholesalePrice ? `S/ ${decimalLabel(item.wholesalePrice, 2)}` : 'Sin configurar'}</small></div>
        <Button variant="secondary" disabled={write.busy || (write.pending && task !== null && selected !== item.id)} onClick={() => openDetail(item.id)}>Ver detalle y presentaciones</Button>
      </li>)}</ul> : <div className="catalog-table" role="region" aria-label="Lista de productos" tabIndex={0}><table className="business-product-table"><thead><tr><th scope="col">Producto / código</th><th scope="col" className="catalog-extra">Categoría</th><th scope="col" className="catalog-extra">Marca / lab.</th><th scope="col" className="catalog-price" aria-label="Precio por unidad base">Precio base</th><th scope="col">Estado</th><th scope="col">Detalle</th></tr></thead><tbody>{visibleProducts.map(item => <tr key={item.id}>
        <td>{item.name}<small>{item.internalCode} · {item.productType === 'medicine' ? 'Medicamento' : 'Venta general'}</small></td><td className="catalog-extra">{categoryName(item.categoryId)}</td><td className="catalog-extra">{item.brandOrLaboratory}</td>
        <td className="catalog-price"><span>S/ {decimalLabel(item.retailPrice, 2)}</span>{item.wholesalePrice && <small>Mayorista S/ {decimalLabel(item.wholesalePrice, 2)}</small>}</td><td><Badge tone={item.isActive ? 'success' : 'neutral'}>{item.isActive ? 'Activo' : 'Inactivo'}</Badge></td>
        <td><Button variant="quiet" disabled={write.busy || (write.pending && task !== null && selected !== item.id)} aria-label={`Ver ${item.name}`} onClick={() => openDetail(item.id)}>Ver</Button></td>
      </tr>)}</tbody></table></div>}</>}
    </>}
    </DataSection>
    <Drawer open={detailOpen} title="Detalle del producto" onClose={() => setDetailOpen(false)}>
      {detail.isPending && <p role="status">Consultando detalle…</p>}
      {detail.isError && <ApiErrorState error={detail.error} title="No pudimos consultar este producto" retry={() => void detail.refetch()} />}
      {detail.data && !detail.isError && <ProductDetailPanel key={detail.data.product.id} detail={detail.data} category={categoryName(detail.data.product.categoryId)} write={write} onEdit={edit} />}
    </Drawer>
    <Modal open={taskOpen && task !== null} wide={task === 'units'} title={task === 'metadata' ? 'Corregir datos locales' : task === 'prices' ? 'Editar precios' : task === 'units' ? 'Configurar presentaciones' : 'Cambiar estado del producto'} onClose={() => { setTaskOpen(false); setTask(null); setDirty(false) }} onRecoveryClose={() => setTaskOpen(false)} dirty={dirty} busy={write.busy} recovery={write.pending}>
      <div className="catalog-body">
        {write.error != null && <ApiErrorState error={write.error} />}{recoverySurface === 'task' && recovery}{write.notice && <p role="status">{write.notice}</p>}
        {taskDetail && <>
          {task === 'metadata' && <EditLocalProduct key={editEpoch} detail={taskDetail} categories={categories.data ?? []} write={write} onDirty={setDirty} />}
          {task === 'prices' && <EditPrices key={editEpoch} detail={taskDetail} write={write} onDirty={setDirty} />}
          {task === 'status' && <EditStatus key={editEpoch} detail={taskDetail} write={write} />}
          {task === 'units' && <UnitEditor key={editEpoch} detail={taskDetail} write={write} onDirty={setDirty} />}
        </>}
      </div>
    </Modal>
  </div>
}
