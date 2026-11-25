import { useWorkspace } from '../access'
import { Panel } from '../../shared/ui/Panel'
import { TenantStructure } from './TenantStructure'

export function SettingsPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert"><h1>Esta sección requiere otro acceso</h1><p>Configuración requiere Owner.</p></Panel>
  return <TenantStructure key={`${w.session.userId}/${w.access.membershipId}/${w.tenantId}/${w.branchId}`} access={w.access}
    session={w.session} tenantName={w.tenantName} workspaceBase={w.base} />
}
