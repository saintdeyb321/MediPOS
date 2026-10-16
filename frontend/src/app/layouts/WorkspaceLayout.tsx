import { ShieldCheck } from 'lucide-react'
import { WorkspaceContext, useOperationalWorkspace, accessMessage, ApiErrorState } from '../../features/access'
import type { Workspace } from '../../features/access'
import { BackLink } from '../../shared/ui/BackLink'
import { LoadingState } from '../../shared/ui/States'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { SelectionLayout } from './SelectionLayout'
import { WorkspaceShell } from './WorkspaceShell'

export function WorkspaceLayout() {
  const { tenantId, branchId, valid, tenants, branches, access, session } = useOperationalWorkspace()
  const back = <BackLink to={`/boticas/${tenantId}/sucursales`}>Elegir otra sucursal</BackLink>
  if (!valid) return (
    <SelectionLayout>
      <BackLink to="/seleccionar-botica">Elegir botica</BackLink>
      <div className="state-block"><h1>Dirección no válida</h1><p>Selecciona una botica y sucursal para continuar.</p></div>
    </SelectionLayout>
  )
  if (access.isPending || branches.isPending || session.isPending) return <LoadingState label="Verificando acceso a la sucursal…" />
  if (access.isError || branches.isError || session.isError) return (
    <SelectionLayout>
      {back}
      <ApiErrorState error={access.error ?? branches.error ?? session.error} retry={() => { void access.refetch(); void branches.refetch(); void session.refetch() }} />
    </SelectionLayout>
  )
  const branch = branches.data?.find(b => b.id === branchId)
  const context = access.data.context
  if (!access.data.isAllowed || !context || !branch || context.userId !== session.data?.userId || context.tenantId !== tenantId || context.branchId !== branchId) return (
    <SelectionLayout>
      {back}
      <Panel className="state-block" role="alert">
        <span className="state-icon warning"><ShieldCheck aria-hidden="true" /></span>
        <h1>Tu acceso está restringido</h1>
        <p>{accessMessage(access.data.isAllowed ? 'access.context_changed' : access.data.code)}</p>
        <Button variant="secondary" onClick={() => void access.refetch()}>Comprobar acceso de nuevo</Button>
      </Panel>
    </SelectionLayout>
  )
  const tenant = tenants.data?.pages.flatMap(page => page.items).find(t => t.tenantId === tenantId)
  const workspace: Workspace = {
    tenantId, branchId, tenantName: tenant?.tradingName ?? 'Botica seleccionada',
    branch, access: context, session: session.data!, base: `/boticas/${tenantId}/sucursales/${branchId}`,
  }
  return <WorkspaceContext.Provider value={workspace}><WorkspaceShell workspace={workspace} /></WorkspaceContext.Provider>
}
