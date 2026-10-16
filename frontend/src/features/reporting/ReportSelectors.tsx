import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { catalogApi } from '../catalog'
import { employeesApi } from '../employees'
import { ApiError } from '../../shared/api/http'
import { scopedKey } from '../../shared/api/contracts'
import { PageControls } from '../../shared/ui/PageControls'
import type { ReportFilters } from './filters'

export function ReportSelectors({ filters: f, change }: { filters: ReportFilters; change: (value: Partial<ReportFilters>) => void }) {
  return <>
    {['sales', 'stock-risk', 'commissions'].includes(f.kind) && <ProductSelector selected={f.productId} change={value => change({ productId: value })} />}
    {['sales', 'commissions'].includes(f.kind) && <EmployeeSelector selected={f.employeeId} change={value => change({ employeeId: value })} />}
    {f.kind === 'sales' && <CategorySelector selected={f.categoryId} change={value => change({ categoryId: value })} />}
  </>
}
function ProductSelector({ selected, change }: { selected: string; change: (value: string) => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0)
  const query = useQuery({ queryKey: scopedKey(w, 'report-products', w.session.userId, w.access.membershipId, offset), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).list(w, offset, 20, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section aria-label="Filtro de producto"><label htmlFor="report-product">Producto opcional del negocio</label><select id="report-product" value={selected} disabled={query.isError || query.isFetching} onChange={event => change(event.target.value)}><option value="">Todos los productos</option>{selected && !query.data?.some(row => row.id === selected) && <option value={selected}>Producto consultado en otra página</option>}{!query.isError && query.data?.map(row => <option key={row.id} value={row.id}>{row.name}{row.isActive ? '' : ' · inactivo'}</option>)}</select>
    {query.isError && <ApiErrorState error={query.error} title="No pudimos consultar productos del negocio" />}<PageControls offset={offset} count={query.isError ? undefined : query.data?.length} busy={query.isFetching} change={setOffset} refresh={() => void query.refetch()} /><p>También admite productos inactivos para consultar su historial; no modifica su estado.</p>
  </section>
}
function EmployeeSelector({ selected, change }: { selected: string; change: (value: string) => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0)
  const query = useQuery({ queryKey: scopedKey(w, 'report-employees', w.session.userId, w.access.membershipId, offset), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return employeesApi(http).list(w, offset, 20, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section aria-label="Filtro de vendedor"><label htmlFor="report-employee">Empleado/vendedor opcional</label><select id="report-employee" value={selected} disabled={query.isError || query.isFetching} onChange={event => change(event.target.value)}><option value="">Todos los vendedores autorizados</option>{selected && !query.data?.some(row => row.membershipId === selected) && <option value={selected}>Empleado consultado en otra página</option>}{!query.isError && query.data?.map(row => <option key={row.membershipId} value={row.membershipId}>{row.displayName}{row.isActive ? '' : ' · inactivo'}</option>)}</select>
    {query.isError && <ApiErrorState error={query.error} title="No pudimos consultar vendedores" />}<PageControls offset={offset} count={query.isError ? undefined : query.data?.length} busy={query.isFetching} change={setOffset} refresh={() => void query.refetch()} />
    <p>Solo identidades del directorio oficial. No se incorporan empleados ni se solicitan correos/UUID libres.</p></section>
}
function CategorySelector({ selected, change }: { selected: string; change: (value: string) => void }) {
  const w = useWorkspace(); const { http } = useRuntime(); const [offset, setOffset] = useState(0)
  const query = useQuery({ queryKey: scopedKey(w, 'report-categories', w.session.userId, w.access.membershipId, offset), queryFn: ({ signal }) => { if (!http) throw new ApiError(0, 'client.https_required'); return catalogApi(http).categories(w, offset, 100, signal) }, enabled: Boolean(http), retry: false, staleTime: 0, refetchOnWindowFocus: false })
  return <section aria-label="Filtro de categoría"><label htmlFor="report-category">Categoría opcional</label><select id="report-category" value={selected} disabled={query.isError || query.isFetching} onChange={event => change(event.target.value)}><option value="">Todas las categorías</option>{selected && !query.data?.some(row => row.id === selected) && <option value={selected}>Categoría consultada en otra página</option>}{!query.isError && query.data?.map(row => <option key={row.id} value={row.id}>{row.name}{row.isActive ? '' : ' · inactiva'}</option>)}</select>
    {query.isError && <ApiErrorState error={query.error} title="No pudimos consultar categorías" />}<PageControls size={100} offset={offset} count={query.isError ? undefined : query.data?.length} busy={query.isFetching} change={setOffset} refresh={() => void query.refetch()} />
  </section>
}
