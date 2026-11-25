import { useState } from 'react'
import { ArrowRight, Store } from 'lucide-react'
import { Link, Navigate, useSearchParams } from 'react-router'
import { ApiErrorState, roleLabels } from '../access'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { SearchField } from '../../shared/ui/TextField'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { useTenants } from './useTenants'

export function TenantSelection() {
  const { tenants, rows } = useTenants()
  const [params] = useSearchParams()
  const explicit = params.has('elegir')
  const [filter, setFilter] = useState('')
  const visible = rows.filter(tenant => tenant.tradingName.toLocaleLowerCase('es-PE').includes(filter.trim().toLocaleLowerCase('es-PE')))
  if (!explicit && tenants.isSuccess && !tenants.isFetching && rows.length === 1 && !tenants.hasNextPage)
    return <Navigate to={`/boticas/${rows[0]!.tenantId}/sucursales`} replace />
  if (tenants.isPending || !explicit && tenants.isFetching && rows.length <= 1)
    return <LoadingState label="Buscando tus boticas…" />
  return <>
    <PageHeader title="Selecciona tu botica" description={rows.length > 1 || tenants.hasNextPage ? 'Elige dónde vas a trabajar.' : undefined} />
    {tenants.isError && <ApiErrorState error={tenants.error} retry={() => void tenants.refetch()} title="No pudimos recuperar tus boticas" />}
    {tenants.isSuccess && !rows.length && <Panel><EmptyState title="Aún no tienes una botica asignada"
      description="Usa la cuenta de Google que recibió la invitación o solicita acceso al responsable de la botica."
      icon={<Store />} /></Panel>}
    {rows.length >= 5 && <SearchField label="Filtrar boticas mostradas" value={filter} onChange={event => setFilter(event.target.value)} />}
    {rows.length > 0 && !visible.length && <p>No hay coincidencias entre las boticas mostradas.</p>}
    {visible.length > 0 && <div className="selection-grid">
      {visible.map(tenant => <Link key={tenant.tenantId} className="selection-card" aria-label={`Entrar a ${tenant.tradingName} como ${roleLabels[tenant.role]}`} to={`/boticas/${tenant.tenantId}/sucursales`}>
        <span className="selection-card-top"><span className="state-icon"><Store size={24} aria-hidden="true" /></span><Badge>{roleLabels[tenant.role]}</Badge></span>
        <span className="selection-title">{tenant.tradingName}</span>
        <span className="selection-card-action">Entrar <ArrowRight size={18} aria-hidden="true" /></span>
      </Link>)}
    </div>}
    {tenants.hasNextPage && <Button variant="secondary" pending={tenants.isFetchingNextPage} onClick={() => void tenants.fetchNextPage()}>Cargar más boticas</Button>}
  </>
}
