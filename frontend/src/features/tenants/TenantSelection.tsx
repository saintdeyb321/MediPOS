import { ArrowRight, Building2, Check, Store } from 'lucide-react'
import { ApiErrorState, roleLabels, useSession } from '../access'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Steps } from '../../shared/ui/Steps'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { useTenants } from './useTenants'

export function TenantSelection() {
  const { tenants, rows, select } = useTenants()
  const session = useSession()
  let content
  if (tenants.isPending) content = <LoadingState label="Buscando tus boticas…" />
  else if (tenants.isError && !rows.length) content = <ApiErrorState error={tenants.error} retry={() => void tenants.refetch()} title="No pudimos recuperar tus boticas" />
  else if (!rows.length) content = (
    <Panel><EmptyState title="Aún no tienes boticas asignadas" description="Pide al propietario que registre tu cuenta. Cuando tengas una membresía activa, aparecerá aquí." icon={<Store />} action={<Button variant="secondary" onClick={() => void tenants.refetch()}>Actualizar mis boticas</Button>} /></Panel>
  )
  else content = (
    <>
      <div className="selection-grid">
        {rows.map(tenant => (
          <button type="button" key={tenant.tenantId} className="selection-card" onClick={() => void select(tenant.tenantId)}>
            <span className="selection-card-top"><span className="state-icon"><Store size={26} aria-hidden="true" /></span><Badge>{roleLabels[tenant.role]}</Badge></span>
            <span className="selection-title">{tenant.tradingName}</span>
            <span className="selection-description"><Building2 size={18} aria-hidden="true" /> Botica asignada a tu cuenta</span>
            <span className="selection-card-action">Seleccionar botica <ArrowRight size={20} aria-hidden="true" /></span>
          </button>
        ))}
      </div>
      {tenants.hasNextPage && <Button variant="secondary" pending={tenants.isFetchingNextPage} onClick={() => void tenants.fetchNextPage()}>Cargar más boticas</Button>}
      {tenants.isError && <ApiErrorState error={tenants.error} retry={() => void tenants.fetchNextPage()} />}
      <p className="selection-note"><Check size={18} aria-hidden="true" />Tu acceso se comprueba de nuevo al elegir una sucursal y en cada operación.</p>
    </>
  )
  return (
    <>
      <Steps labels={['Botica', 'Sucursal', 'Tu espacio']} current={0} />
      <PageHeader eyebrow="TU ESPACIO DE TRABAJO" title="¿En qué botica trabajamos hoy?" description="Elige una de las boticas a las que tienes acceso." />
      {content}
      <details><summary>Mi identidad para el alta de Owner</summary><p>Inicia sesión en este entorno antes del alta. Comparte este identificador solo con el operador autorizado; el servidor verificará la cuenta Google registrada.</p><output>{session.data?.userId}</output></details>
    </>
  )
}
