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
import { EmployeesPage } from '../../features/employees'
import { useWorkspace } from '../../features/access'
import { WorkspaceLayout } from '../layouts/WorkspaceLayout'
import { SelectionLayout } from '../layouts/SelectionLayout'
import { availableNavigation } from '../navigation'
import { ModulePage } from './ModulePage'

function DashboardRoute() {
  const { access } = useWorkspace()
  const shortcuts = availableNavigation(access.role).filter(item => item.id && item.id !== 'ventas')
  return <DashboardPage shortcuts={shortcuts} />
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<LoginPage />} />
      <Route element={<SessionGate />}>
        <Route path="/seleccionar-botica" element={<SelectionLayout><TenantSelection /></SelectionLayout>} />
        <Route path="/boticas/:tenantId/sucursales" element={<SelectionLayout><BranchSelection /></SelectionLayout>} />
        <Route path="/boticas/:tenantId/sucursales/:branchId" element={<WorkspaceLayout />}>
          <Route index element={<DashboardRoute />} />
          <Route path="ventas" element={<PosPage />} />
          <Route path="caja" element={<CashPage />} />
          <Route path="productos" element={<ProductsPage />} />
          <Route path="compras" element={<PurchasesPage />} />
          <Route path="inventario" element={<InventoryPage />} />
          <Route path="empleados" element={<EmployeesPage />} />
          <Route path=":module" element={<ModulePage />} />
        </Route>
      </Route>
      <Route path="*" element={<main className="state-block"><h1>No encontramos esta página</h1><p>Regresa al inicio para continuar.</p><Link className="button primary" to="/">Ir al inicio</Link></main>} />
    </Routes>
  )
}
