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
  const [category, setCategory] = useState(''); const [offset, setOffset] = useState(0); const [categoryOffset, setCategoryOffset] = useState(0)
  const enabled = Boolean(http)
  const key = (name: string) => scopedKey(w, name, w.session.userId, w.access.membershipId, offset, category)
  const categories = useQuery({ queryKey: scopedKey(w, 'pos-categories', w.session.userId, w.access.membershipId, categoryOffset), queryFn: ({ signal }) => catalogApi(http!).operationalCategories(w, categoryOffset, 100, signal), enabled, retry: false, staleTime: 60_000, refetchOnWindowFocus: false })
  const products = useQuery({ queryKey: key('pos-catalog'), queryFn: ({ signal }) => catalogApi(http!).operationalList(w, '', category, offset, 50, signal), enabled, retry: false, staleTime: 30_000, refetchOnWindowFocus: false })

  const rows = products.data
  return <section className="pos-browse" aria-label="Explorar catálogo por categoría">
    <p className="data-caption">Referencias de catálogo · consulta stock antes de agregar.</p>
    <div className="category-chips" role="group" aria-label="Categorías disponibles"><Button variant="quiet" aria-pressed={!category} onClick={() => { setCategory(''); setOffset(0) }}>Todas</Button>{!categories.isError && categories.data?.filter(row => row.isActive).map(row => <Button key={row.id} variant="quiet" aria-pressed={category === row.id} onClick={() => { setCategory(row.id); setOffset(0) }}>{row.name}</Button>)}</div>
    {categories.isError && <ApiErrorState error={categories.error} title="No pudimos consultar categorías" retry={() => void categories.refetch()} />}
    {categories.data?.length === 100 && <p className="data-caption">Se muestran las primeras 100 categorías del directorio.</p>}
    {products.isFetching && <p className="loading-strip" role="status">Consultando catálogo…</p>}
    {products.isError && <ApiErrorState error={products.error} title="No pudimos consultar productos" retry={() => void products.refetch()} />}
    {!products.isError && !products.isFetching && rows && <>{!rows.length ? <EmptyState title="Sin productos en esta página" description="Prueba otra categoría, cambia de página o utiliza el buscador." /> : <ul className="pos-catalog-grid">{rows.map(row => <li key={row.id} className="pos-catalog-card"><span className="product-placeholder">{row.productType === 'medicine' ? <Pill size={28} aria-hidden="true" /> : <Package size={28} aria-hidden="true" />}</span><small>{categories.data?.find(c => c.id === row.categoryId)?.name ?? 'Categoría no disponible'}</small><h3>{row.name}</h3><p>{row.brandOrLaboratory}</p><strong>S/ {decimalLabel(row.retailPrice, 2)} <small>/ unidad base</small></strong><Button variant="secondary" disabled={disabled} onClick={() => search(row.internalCode)}>Consultar stock y elegir</Button></li>)}</ul>}</>}
    <p className="data-caption">Categoría aplicada por el servidor sobre el catálogo autorizado. Consulta stock para agregar a esta sucursal.</p><PageControls size={100} offset={categoryOffset} count={categories.data?.length} busy={categories.isFetching} change={setCategoryOffset} refresh={() => void categories.refetch()} />
    <PageControls size={50} offset={offset} count={products.isError ? undefined : products.data?.length} busy={products.isFetching} change={setOffset} refresh={() => void products.refetch()} />
  </section>
}
