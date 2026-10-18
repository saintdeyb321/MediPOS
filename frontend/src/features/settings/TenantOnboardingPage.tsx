import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { ApiErrorState, isUuid, parseAccess, useRuntime, useSession } from '../access'
import { useTenants } from '../tenants'
import { BackLink } from '../../shared/ui/BackLink'
import { Notice } from '../../shared/ui/Notice'
import { LoadingState } from '../../shared/ui/States'
import { TenantSettings } from './SettingsPage'

export function TenantOnboardingPage() {
  const { tenantId = '' } = useParams(); const { http } = useRuntime(); const session = useSession(); const { rows } = useTenants()
  const access = useQuery({
    queryKey: ['tenant', tenantId, 'onboarding-access', session.data?.userId],
    queryFn: async ({ signal }) => parseAccess(await http!.request(`/api/tenants/${tenantId}/access`, { signal }), tenantId, null),
    enabled: Boolean(http && session.data && isUuid(tenantId)), retry: false, refetchOnWindowFocus: false, refetchInterval: 60_000,
  })
  if (!isUuid(tenantId) || !http) return <Notice tone="error">Se requiere una botica válida y conexión HTTPS.</Notice>
  if (access.isPending) return <LoadingState label="Verificando acceso Owner sin sucursal…" />
  if (access.isError) return <ApiErrorState error={access.error} retry={() => void access.refetch()} />
  const context = access.data.context; const currentSession = session.data
  if (!currentSession || !access.data.isAllowed || !context || context.role !== 'owner' || !context.membershipIsActive || context.userId !== currentSession.userId)
    return <Notice tone="error">La configuración requiere una membresía Owner activa y licencia vigente. Vuelve a seleccionar tu botica o solicita la revisión de tu acceso.</Notice>
  return <>
    <BackLink to={`/boticas/${tenantId}/sucursales`}>Volver a sucursales</BackLink>
    <p>No necesitas una sucursal previa. Registra la entidad, crea la sede y selecciónala para operar. Cada petición vuelve a autorizarse en el servidor.</p>
    <TenantSettings key={`${currentSession.userId}/${context.membershipId}/${tenantId}`} title={`${rows.find(row => row.tenantId === tenantId)?.tradingName ?? 'Tu botica'} · primera sede y configuración`} scope={{ tenantId }} userId={currentSession.userId} membershipId={context.membershipId} />
  </>
}
