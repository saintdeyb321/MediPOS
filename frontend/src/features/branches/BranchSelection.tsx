import { ArrowRight, MapPin, Plus } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Link, Navigate, useSearchParams } from 'react-router'
import { accessMessage, ApiErrorState, roleLabels, useRuntime, useTenantAccess, useTenantDirectory, verifiedAccess } from '../access'
import { BackLink } from '../../shared/ui/BackLink'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { useBranches } from './useBranches'

export function BranchSelection() {
  const { tenantId, valid, branches, session } = useBranches()
  const { service } = useRuntime()
  const [params] = useSearchParams()
  const explicit = params.has('elegir')
  const rows = branches.isSuccess && branches.isFetchedAfterMount && !branches.isFetching ? branches.data : undefined
  const single = !explicit && rows?.length === 1 ? rows[0] : undefined
  const tenantDirectory = useTenantDirectory(session.data)
  const tenantName = tenantDirectory.data?.pages.flatMap(page => page.items).find(t => t.tenantId === tenantId)?.tradingName
  const tenantAccess = useTenantAccess(tenantId, session.data, Boolean(rows && !single))
  const access = useQuery({
    queryKey: ['tenant', tenantId, 'entry-access', single?.id, session.data?.userId, session.data?.expiresAtUtc],
    queryFn: ({ signal }) => service.access(tenantId, single!.id, signal),
    enabled: Boolean(single && session.data), staleTime: 0, refetchOnMount: 'always',
  })
  const tenantContext = verifiedAccess(tenantAccess.data, session.data, tenantId, null)
  const owner = tenantAccess.isSuccess && !tenantAccess.isFetching && tenantAccess.data.isAllowed && tenantContext?.role === 'owner'
  const branchContext = single && verifiedAccess(access.data, session.data, tenantId, single.id)
  const root = `/boticas/${tenantId}`
  if (!valid) return <Notice tone="error">El identificador de botica no es válido.</Notice>
  if (single && access.isSuccess && access.isFetchedAfterMount && !access.isFetching && access.data.isAllowed && branchContext)
    return <Navigate to={`${root}/sucursales/${single.id}`} replace />
  if (!explicit && rows?.length === 0 && owner)
    return <Navigate to={`${root}/configuracion`} replace />

  const back = <BackLink to="/seleccionar-botica?elegir=1">Cambiar botica</BackLink>
  if (branches.isError) return <>{back}<ApiErrorState error={branches.error} retry={() => void branches.refetch()} /></>
  if (!rows || single && (access.isPending || access.isFetching) || rows.length === 0 && (tenantAccess.isPending || tenantAccess.isFetching))
    return <LoadingState label={single ? 'Verificando acceso a la sucursal…' : 'Buscando tus sucursales…'} />
  return <>
    {back}
    <PageHeader title={single ? 'Acceso a la sucursal' : owner && rows.length ? 'Mis sucursales' : rows.length ? 'Selecciona una sucursal' : 'Sucursales'}
      description={tenantName} action={owner && rows.length > 0 && <Link className="button primary" to={`${root}/configuracion#sucursales`}>Gestionar sucursales</Link>} />
    {single ? (
      access.isError ? <ApiErrorState error={access.error} retry={() => void access.refetch()} /> :
        <Panel><EmptyState title="Tu acceso está restringido"
          description={accessMessage(access.data?.isAllowed ? 'access.context_changed' : access.data?.code ?? '', branchContext ? branchContext.role : undefined)}
          action={<Button variant="secondary" onClick={() => void access.refetch()}>Comprobar acceso de nuevo</Button>} /></Panel>
    ) : rows.length === 0 ? (
      tenantAccess.isError ? <ApiErrorState error={tenantAccess.error} retry={() => void tenantAccess.refetch()} /> :
        owner ? <Panel><EmptyState title="Tu botica está casi lista" description="Crea tu primera sucursal para empezar a vender y controlar tu inventario."
          action={<Link className="button primary" to={`${root}/configuracion`}><Plus size={16} aria-hidden="true" />Crear mi primera sucursal</Link>} /></Panel> :
          tenantContext && tenantContext.role !== 'owner' && tenantAccess.data?.code === 'access.branch_required'
            ? <Panel><EmptyState title="Todavía no tienes una sucursal asignada" description="Pide al propietario que te asigne una sucursal." /></Panel>
            : <Panel><EmptyState title="Tu acceso está restringido"
                description={accessMessage(tenantAccess.data?.isAllowed ? 'access.context_changed' : tenantAccess.data?.code ?? 'access.context_changed', tenantContext?.role)} /></Panel>
    ) : <div className="branch-list">
      {rows.map(branch => <Link className="branch-option" to={`${root}/sucursales/${branch.id}`} key={branch.id}>
        <span className="state-icon"><MapPin aria-hidden="true" /></span>
        <div><h2>{branch.name}</h2><span className="text-link">Entrar</span></div>
        {branch.isMainHub && <Badge tone="info">Sede principal</Badge>}
        {tenantContext && <Badge>{roleLabels[tenantContext.role]}</Badge>}
        <ArrowRight size={18} aria-hidden="true" />
      </Link>)}
    </div>}
  </>
}
