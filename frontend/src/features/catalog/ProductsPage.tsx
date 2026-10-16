import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { TextField } from '../../shared/ui/TextField'
import { catalogApi } from './api'
import type { Category, GlobalProduct } from './api'
import { useCatalogWrite } from './useCatalogWrite'
import { CreateProductForm } from './CreateProductForm'
import { ProductDetailPanel } from './ProductDetailPanel'
import './catalog.css'

export function ProductsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Productos requiere acceso de propietario</h1><p>El servidor conserva la autorización de cada operación.</p></Panel>
  return <CatalogWorkspace key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} />
}
function CatalogWorkspace() {
  const w = useWorkspace(); const { http } = useRuntime()
  const [offset, setOffset] = useState(0); const [globalOffset, setGlobalOffset] = useState(0)
  const [mode, setMode] = useState<'products' | 'create' | 'global'>('products')
  const [selected, setSelected] = useState(''); const [global, setGlobal] = useState<GlobalProduct | null>(null)
  const [reviewed, setReviewed] = useState(false)
  const [recoveryCode, setRecoveryCode] = useState('')
  const write = useCatalogWrite(id => { setSelected(id); setMode('products'); setGlobal(null) })
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
  return <div className="catalog-page">
    <PageHeader title="Productos" description={`Catálogo del negocio · ${w.tenantName}. Precios por unidad base; este listado no devuelve stock.`} />
    {!http && <Notice tone="warning">Esta pantalla requiere conexión HTTPS real con MediPOS.</Notice>}
    {write.error != null && <ApiErrorState error={write.error} title="No pudimos completar la operación de catálogo" />}
    {write.notice && <p role="status">{write.notice}</p>}
    {write.pending && <Panel><h2>Operación pendiente de conciliación</h2><p>No reenvíes la solicitud. Si falta el identificador, actualiza el listado y localiza el código interno original. Una lectura que no encuentra cambios no demuestra que la escritura falló.</p>
      <Button variant="secondary" disabled={write.busy} onClick={() => { setReviewed(false); void write.consult(selected || undefined) }}>Consultar producto de la operación</Button>
      {write.observed && <><p>Producto consultado: {write.observed.product.name} · Código {write.observed.product.internalCode}. Revisa todos sus datos en el detalle.</p>
        <TextField label="Código interno de la solicitud original" value={recoveryCode} maxLength={64} onChange={event => { setRecoveryCode(event.target.value); setReviewed(false) }} />
        <label><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> Verifiqué el código y los atributos contra la solicitud original; la operación quedó conciliada, sin otra solicitud en curso.</label>
        <Button variant="secondary" disabled={!reviewed || write.busy || !write.canAcknowledge || recoveryCode.trim() !== write.observed.product.internalCode} onClick={() => { write.acknowledge(recoveryCode); setReviewed(false); setRecoveryCode('') }}>Continuar tras conciliación verificada</Button></>}
    </Panel>}
    <nav aria-label="Catálogo"><Button variant="secondary" onClick={() => setMode('products')}>Productos del negocio</Button><Button variant="secondary" disabled={write.locked} onClick={() => setMode('create')}>Nuevo producto local</Button><Button variant="secondary" onClick={() => setMode('global')}>Catálogo global</Button></nav>
    {categories.isError && <ApiErrorState error={categories.error} title="No pudimos consultar las categorías" />}
    {categories.isError && <Button variant="quiet" onClick={() => void categories.refetch()}>Consultar categorías</Button>}
    {mode === 'create' && <Panel><div className="catalog-body">{categories.isPending ? <p>Consultando categorías…</p> : categories.data && !categories.isError && <CreateProductForm categories={categories.data} global={null} write={write} />}</div></Panel>}
    {mode === 'global' && <Panel><div className="catalog-body"><h2>Catálogo global disponible</h2><p>Referencia global, sin precios privados ni existencias físicas. Asociar crea un producto propio, no stock.</p>
      {globals.isPending && <p role="status">Consultando catálogo global…</p>}
      {globals.isError && <ApiErrorState error={globals.error} title="No pudimos consultar el catálogo global" />}
      {globals.data && !globals.isError && <>{!globals.data.length && <p>No hay productos globales en esta página.</p>}
        <ul>{globals.data.map(item => <li key={item.id}>{item.name} · {categoryName(item.categoryId)} · {item.brandOrLaboratory} <Button variant="quiet" disabled={write.locked} onClick={() => setGlobal(item)}>Seleccionar referencia {item.name}</Button></li>)}</ul></>}
      <Pagination offset={globalOffset} change={setGlobalOffset} count={globals.data?.length} busy={globals.isFetching} refresh={() => void globals.refetch()} />
      {global && <CreateProductForm key={global.id} categories={categories.data ?? []} global={global} write={write} />}
    </div></Panel>}
    <Panel><div className="catalog-body"><h2>Productos del negocio</h2>
      {products.isPending && <p role="status">Consultando productos…</p>}
      {products.isError && <ApiErrorState error={products.error} title="No pudimos consultar los productos" />}
      {products.data && !products.isError && <>{!products.data.length ? <p>No hay productos en esta página.</p> : <div className="catalog-table"><table><caption>Precios por unidad base · stock no incluido en esta respuesta</caption><thead><tr><th>Producto/código</th><th>Categoría/tipo</th><th>Marca o laboratorio</th><th>Precios base</th><th>Estado</th><th>Detalle</th></tr></thead><tbody>{products.data.map(item => <tr key={item.id}>
        <td>{item.name}<br />{item.internalCode}</td><td>{categoryName(item.categoryId)}<br />{item.productType === 'medicine' ? 'Medicamento' : 'Venta general'}</td><td>{item.brandOrLaboratory}</td>
        <td>Minorista S/ {decimalLabel(item.retailPrice, 2)}<br />Mayorista {item.wholesalePrice ? `S/ ${decimalLabel(item.wholesalePrice, 2)}` : 'No configurado'}</td><td>{item.isActive ? 'Activo' : 'Inactivo'}</td>
        <td><Button variant="quiet" disabled={write.busy} onClick={() => { setReviewed(false); setSelected(item.id) }}>Ver {item.name}</Button></td>
      </tr>)}</tbody></table></div>}</>}
      <Pagination offset={offset} change={setOffset} count={products.data?.length} busy={products.isFetching} refresh={() => void products.refetch()} />
    </div></Panel>
    {selected && <Panel><div className="catalog-body">
      {detail.isPending && <p role="status">Consultando detalle…</p>}
      {detail.isError && <ApiErrorState error={detail.error} title="No pudimos consultar este producto" />}
      {detail.data && !detail.isError && <ProductDetailPanel detail={detail.data} write={write} />}
      <Button variant="quiet" disabled={detail.isFetching} onClick={() => void detail.refetch()}>Actualizar detalle</Button>
    </div></Panel>}
  </div>
}
function Pagination({ offset, change, count, busy, refresh }: { offset: number; change: (value: number) => void; count?: number; busy: boolean; refresh: () => void }) {
  return <div className="catalog-actions"><Button variant="quiet" disabled={busy} onClick={refresh}>Actualizar lista</Button>
    <Button variant="quiet" disabled={busy || offset === 0} onClick={() => change(offset - 50)}>Página anterior</Button><span>Página {offset / 50 + 1}</span>
    <Button variant="quiet" disabled={busy || count !== 50 || offset >= 10000} onClick={() => change(offset + 50)}>Página siguiente</Button></div>
}
