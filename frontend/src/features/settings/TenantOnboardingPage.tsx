import { useParams } from 'react-router'
import { accessMessage, ApiErrorState, isUuid, useRuntime, useSession, useTenantAccess, useTenantDirectory, verifiedAccess } from '../access'
import { BackLink } from '../../shared/ui/BackLink'
import { Notice } from '../../shared/ui/Notice'
import { LoadingState } from '../../shared/ui/States'
import { TenantStructure } from './TenantStructure'

export function TenantOnboardingPage() {
  const { tenantId = '' } = useParams()
  const { http } = useRuntime()
  const session = useSession()
  const access = useTenantAccess(tenantId, session.data)
  const context = verifiedAccess(access.data, session.data, tenantId, null)
  const owner = Boolean(context?.role === 'owner' && access.data?.isAllowed && access.isSuccess && access.isFetchedAfterMount)
  const tenants = useTenantDirectory(owner ? session.data : null)
  const back = <BackLink to={`/boticas/${tenantId}/sucursales?elegir=1`}>Volver a sucursales</BackLink>
  if (!isUuid(tenantId) || !http) return <Notice tone="error">Se requiere una botica válida y conexión HTTPS.</Notice>
  if (access.isPending || access.isFetching && !access.isFetchedAfterMount) return <LoadingState label="Verificando acceso a configuración…" />
  if (access.isError) return <>{back}<ApiErrorState error={access.error} retry={() => void access.refetch()} /></>
  if (!owner || !context || !session.data)
    return <>{back}<Notice tone="error">{access.data?.isAllowed || access.data?.code === 'access.branch_required'
      ? 'Solo el propietario de la botica puede cambiar esta configuración.' : accessMessage(access.data?.code ?? 'access.context_changed', context?.role)}</Notice></>
  const tenantName = tenants.data?.pages.flatMap(page => page.items).find(row => row.tenantId === tenantId)?.tradingName
  return <>{back}<TenantStructure key={`${session.data.userId}/${context.membershipId}/${tenantId}`} access={context}
    session={session.data} tenantName={tenantName} /></>
}
