import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { ArrowRight, Building2, Check, MapPin, Store, UserRound, LogOut } from 'lucide-react'
import { Brand, BackLink, ErrorState, Loading, PageHeading } from '../../components/ui'
import { useRuntime } from '../../hooks/useSession'
import { isUuid, roleLabels } from '../../types/contracts'
import type { ReactNode } from 'react'
import { LogoutButton } from '../auth/LogoutButton'

export function SelectionLayout({ children }: { children: ReactNode }) {
  const { preview } = useRuntime()
  return <div className="selection-page"><header className="selection-header"><Link to="/seleccionar-botica" aria-label="MediPOS, elegir botica"><Brand /></Link><span className="selection-account"><UserRound size={17} />{preview ? 'Sesión de ejemplo' : 'Mi cuenta'}<LogoutButton icon={LogOut} /></span></header><main id="contenido" className="selection-content">{children}</main><footer className="selection-footer"><span>Tu botica, en orden.</span><span>MediPOS · Perú</span></footer></div>
}
export function TenantSelection() {
  const { service } = useRuntime()
  const client = useQueryClient(); const navigate = useNavigate()
  const tenants = useInfiniteQuery({ queryKey: ['tenants'], initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) => service.tenants(pageParam, signal),
    getNextPageParam: page => page.nextAfterTenantId ?? undefined,
  })
  const rows = tenants.data?.pages.flatMap(page => page.items) ?? []
  async function select(id: string) {
    await client.cancelQueries({ queryKey: ['tenant'] }); client.removeQueries({ queryKey: ['tenant'] })
    navigate(`/boticas/${id}/sucursales`)
  }
  return <SelectionLayout><div className="steps"><span className="active">1 <i>Botica</i></span><div /><span>2 <i>Sucursal</i></span><div /><span>3 <i>Tu espacio</i></span></div>
    <PageHeading eyebrow="TU ESPACIO DE TRABAJO" title="¿En qué botica trabajamos hoy?" description="Elige una de las boticas a las que tienes acceso." />
    {tenants.isPending ? <Loading label="Buscando tus boticas…" /> : tenants.isError && !rows.length ? <ErrorState error={tenants.error} retry={() => void tenants.refetch()} title="No pudimos recuperar tus boticas" /> : !rows.length ? <div className="panel state-block"><span className="state-icon"><Store /></span><h2>Aún no tienes boticas asignadas</h2><p>Pide al propietario que registre tu cuenta. Cuando tengas una membresía activa, aparecerá aquí.</p><button className="button secondary" onClick={() => void tenants.refetch()}>Actualizar mis boticas</button></div> : <>
      <div className="selection-grid">{rows.map(tenant => <button key={tenant.tenantId} className="selection-card" onClick={() => void select(tenant.tenantId)}><span className="selection-card-icon"><Store size={28} /></span><span className="badge neutral">{roleLabels[tenant.role]}</span><h2>{tenant.tradingName}</h2><p><Building2 size={15} /> Botica asignada a tu cuenta</p><span className="selection-card-action">Seleccionar botica <ArrowRight size={18} /></span></button>)}</div>
      {tenants.hasNextPage && <button className="button secondary" disabled={tenants.isFetchingNextPage} onClick={() => void tenants.fetchNextPage()}>Cargar más boticas</button>}
      {tenants.isError && <ErrorState error={tenants.error} retry={() => void tenants.fetchNextPage()} />}
      <p className="selection-note"><Check size={15} /> Tu acceso se comprueba de nuevo al elegir una sucursal y en cada operación.</p></>}
  </SelectionLayout>
}
export function BranchSelection() {
  const { tenantId = '' } = useParams(); const { service } = useRuntime()
  const valid = isUuid(tenantId)
  const branches = useQuery({ queryKey: ['tenant', tenantId, 'branches'], queryFn: ({ signal }) => service.branches(tenantId, signal), enabled: valid, staleTime: 0 })
  return <SelectionLayout><BackLink to="/seleccionar-botica">Cambiar botica</BackLink><div className="steps"><span className="complete"><Check size={15} /><i>Botica</i></span><div /><span className="active">2 <i>Sucursal</i></span><div /><span>3 <i>Tu espacio</i></span></div>
    <PageHeading eyebrow="ELIGE DÓNDE OPERAR" title="Cada sucursal, bajo control." description="Mostramos únicamente las sucursales autorizadas para tu cuenta. Tu horario se valida al ingresar." />
    {!valid ? <div className="notice error" role="alert">El identificador de botica no es válido.</div> : branches.isPending ? <Loading label="Buscando tus sucursales…" /> : branches.isError ? <ErrorState error={branches.error} retry={() => void branches.refetch()} /> : !branches.data.length ? <div className="panel state-block"><span className="state-icon"><MapPin /></span><h2>No hay sucursales disponibles</h2><p>El propietario debe crear una sucursal o asignarte una para continuar.</p><button className="button secondary" onClick={() => void branches.refetch()}>Actualizar sucursales</button></div> : <div className="branch-list">{branches.data.map(branch => <Link className="branch-option" to={`/boticas/${tenantId}/sucursales/${branch.id}`} key={branch.id}><span className="selection-card-icon"><MapPin /></span><div><h2>{branch.name}</h2><p>Inventario y caja independientes</p></div><span className="badge neutral">Sucursal</span><ArrowRight size={19} /></Link>)}</div>}
  </SelectionLayout>
}
