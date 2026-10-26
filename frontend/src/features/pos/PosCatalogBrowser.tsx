import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Package, Pill } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { catalogApi } from '../catalog'
import { scopedKey } from '../../shared/api/contracts'
import { decimalLabel } from '../../shared/api/decimal'
import { Button } from '../../shared/ui/Button'
import { PageControls } from '../../shared/ui/PageControls'
import { EmptyState } from '../../shared/ui/States'

export function PosCatalogBrowser({ search, disabled }: { search: (code: string) => void; disabled?: boolean }) {
  const w = useWorkspace(); const { http } = useRuntime()
  const [category, setCategory] = useState(''); const [offset, setOffset] = useState(0)
  const enabled = Boolean(http && w.access.role === 'owner')
  const key = (name: string) => scopedKey(w, name, w.session.userId, w.access.membershipId, offset)
  const categories = useQuery({ queryKey: scopedKey(w, 'pos-categories', w.session.userId, w.access.membershipId), queryFn: ({ signal }) => catalogApi(http!).categories(w, 0, 100, signal), enabled, retry: false, staleTime: 60_000, refetchOnWindowFocus: false })
  const products = useQuery({ queryKey: key('pos-catalog'), queryFn: ({ signal }) => catalogApi(http!).list(w, offset, 50, signal), enabled, retry: false, staleTime: 30_000, refetchOnWindowFocus: false })
  if (w.access.role !== 'owner') return <div className="pos-search-help"><Pill size={24} aria-hidden="true" /><p>Busca por nombre, principio activo o código de barras.</p><small>El catálogo inicial por categorías aún no está disponible para tu rol.</small></div>
  const rows = products.data?.filter(row => row.isActive && (!category || row.categoryId === category))
  return <section className="pos-browse" aria-label="Explorar catálogo por categoría">
    <p className="data-caption">Referencias de catálogo · consulta stock antes de agregar.</p>
    <div className="category-chips" role="group" aria-label="Categorías de esta página"><Button variant="quiet" aria-pressed={!category} onClick={() => setCategory('')}>Todas</Button>{!categories.isError && categories.data?.filter(row => row.isActive).map(row => <Button key={row.id} variant="quiet" aria-pressed={category === row.id} onClick={() => setCategory(row.id)}>{row.name}</Button>)}</div>
    {categories.isError && <ApiErrorState error={categories.error} title="No pudimos consultar categorías" retry={() => void categories.refetch()} />}
    {categories.data?.length === 100 && <p className="data-caption">Se muestran las primeras 100 categorías del directorio.</p>}
    {products.isFetching && <p className="loading-strip" role="status">Consultando catálogo…</p>}
    {products.isError && <ApiErrorState error={products.error} title="No pudimos consultar productos" retry={() => void products.refetch()} />}
    {!products.isError && !products.isFetching && rows && <>{!rows.length ? <EmptyState title="Sin productos en esta página" description="Prueba otra categoría, cambia de página o utiliza el buscador." /> : <ul className="pos-catalog-grid">{rows.map(row => <li key={row.id} className="pos-catalog-card"><span className="product-placeholder">{row.productType === 'medicine' ? <Pill size={28} aria-hidden="true" /> : <Package size={28} aria-hidden="true" />}</span><small>{categories.data?.find(c => c.id === row.categoryId)?.name ?? 'Categoría no disponible'}</small><h3>{row.name}</h3><p>{row.brandOrLaboratory}</p><strong>S/ {decimalLabel(row.retailPrice, 2)} <small>/ unidad base</small></strong><Button variant="secondary" disabled={disabled} onClick={() => search(row.internalCode)}>Consultar stock y elegir</Button></li>)}</ul>}</>}
    <p className="data-caption">Filtro sobre esta página de hasta 50 productos; no es una búsqueda global por categoría.</p>
    <PageControls size={50} offset={offset} count={products.isError ? undefined : products.data?.length} busy={products.isFetching} change={setOffset} refresh={() => void products.refetch()} />
  </section>
}
