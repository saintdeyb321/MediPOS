import { Link, Route, Routes } from 'react-router'
import { SessionGate, LoginPage } from '../../features/auth'
import { TenantSelection } from '../../features/tenants'
import { BranchSelection } from '../../features/branches'
import { DashboardPage } from '../../features/dashboard'
import { PosPage } from '../../features/pos'
import { CashPage } from '../../features/cash'
import { ProductsPage } from '../../features/catalog'
import { PurchasesPage } from '../../features/purchasing'
import { InventoryPage } from '../../features/inventory'
import { AcceptInvitationPage, EmployeesPage } from '../../features/employees'
import { CashTransfersPage, TransfersPage } from '../../features/transfers'
import { ReportsPage } from '../../features/reporting'
import { CommissionsPage } from '../../features/commissions'
import { NotificationsPage } from '../../features/notifications'
import { SettingsPage, TenantOnboardingPage } from '../../features/settings'
import { PlatformPage } from '../../features/platform'
import { AuditPage } from '../../features/audit'
import { ImportPage } from '../../features/product-import'
import { ContingencyPage, OfflinePage } from '../../features/offline'
import { OwnerDashboard } from '../../features/reporting'
import { useWorkspace } from '../../features/access'
import { WorkspaceLayout } from '../layouts/WorkspaceLayout'
import { SelectionLayout } from '../layouts/SelectionLayout'
import { availableNavigation } from '../navigation'
import { ModulePage } from './ModulePage'

function DashboardRoute() {
  const { access } = useWorkspace()
  const shortcuts = availableNavigation(access.role).filter(item => ['ventas', 'productos', 'compras', 'inventario'].includes(item.id))
  return <DashboardPage shortcuts={shortcuts} metrics={<OwnerDashboard />} />
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<LoginPage />} />
      <Route path="/aceptar-invitacion" element={<AcceptInvitationPage />} />
      <Route path="/contingencia" element={<ContingencyPage />} />
      <Route element={<SessionGate />}>
        <Route path="/plataforma" element={<SelectionLayout><PlatformPage /></SelectionLayout>} />
        <Route path="/seleccionar-botica" element={<SelectionLayout><TenantSelection /></SelectionLayout>} />
        <Route path="/boticas/:tenantId/sucursales" element={<SelectionLayout><BranchSelection /></SelectionLayout>} />
        <Route path="/boticas/:tenantId/configuracion" element={<SelectionLayout><TenantOnboardingPage /></SelectionLayout>} />
        <Route path="/boticas/:tenantId/sucursales/:branchId" element={<WorkspaceLayout />}>
          <Route index element={<DashboardRoute />} />
          <Route path="ventas" element={<PosPage />} />
          <Route path="caja" element={<CashPage />} />
          <Route path="productos" element={<ProductsPage />} />
          <Route path="compras" element={<PurchasesPage />} />
          <Route path="inventario" element={<InventoryPage />} />
          <Route path="empleados" element={<EmployeesPage />} />
          <Route path="transferencias" element={<TransfersPage />} />
          <Route path="transferencias/sencillo" element={<CashTransfersPage />} />
          <Route path="reportes" element={<ReportsPage />} />
          <Route path="comisiones" element={<CommissionsPage />} />
          <Route path="notificaciones" element={<NotificationsPage />} />
          <Route path="configuracion" element={<SettingsPage />} />
          <Route path="auditoria" element={<AuditPage />} />
          <Route path="importacion" element={<ImportPage />} />
          <Route path="sincronizacion" element={<OfflinePage />} />
          <Route path=":module" element={<ModulePage />} />
        </Route>
      </Route>
      <Route path="*" element={<main className="state-block"><h1>No encontramos esta página</h1><p>Regresa al inicio para continuar.</p><Link className="button primary" to="/">Ir al inicio</Link></main>} />
    </Routes>
  )
}
