import { Link, Route, Routes } from 'react-router'
import { SessionGate } from '../features/auth/SessionGate'
import { LoginPage } from '../features/auth/LoginPage'
import { BranchSelection, TenantSelection } from '../features/workspace/selection'
import { WorkspaceLayout } from '../layouts/WorkspaceLayout'
import { DashboardPage } from '../features/dashboard/DashboardPage'
import { PosPage } from '../features/pos/PosPage'
import { ModulePage } from '../features/workspace/ModulePage'
export function AppRoutes({ onPreview }: { onPreview?: () => void }) {
  return <Routes><Route path="/" element={<LoginPage onPreview={onPreview} />} /><Route element={<SessionGate />}>
    <Route path="/seleccionar-botica" element={<TenantSelection />} />
    <Route path="/boticas/:tenantId/sucursales" element={<BranchSelection />} />
    <Route path="/boticas/:tenantId/sucursales/:branchId" element={<WorkspaceLayout />}>
      <Route index element={<DashboardPage />} /><Route path="ventas" element={<PosPage />} /><Route path=":module" element={<ModulePage />} />
    </Route>
  </Route><Route path="*" element={<main className="state-block"><h1>No encontramos esta página</h1><p>Regresa al inicio para continuar.</p><Link className="button primary" to="/">Ir al inicio</Link></main>} /></Routes>
}
