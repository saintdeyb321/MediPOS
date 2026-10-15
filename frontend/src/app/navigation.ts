import { Banknote, BarChart3, Boxes, ClipboardList, LayoutDashboard, Package, Settings2, ShoppingBasket, UsersRound } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { TenantRole } from '../features/access'
export interface NavigationItem { id: string; label: string; description: string; icon: LucideIcon; roles: TenantRole[]; section: 'operation' | 'management' }
export const navigation: NavigationItem[] = [
  { id: '', label: 'Inicio', description: 'Tu espacio de trabajo', icon: LayoutDashboard, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation' },
  { id: 'ventas', label: 'Punto de venta', description: 'Atiende a tus clientes', icon: ShoppingBasket, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation' },
  { id: 'productos', label: 'Productos', description: 'Tu catálogo y presentaciones', icon: Package, roles: ['owner'], section: 'operation' },
  { id: 'inventario', label: 'Inventario', description: 'Stock, lotes y vencimientos', icon: Boxes, roles: ['owner', 'pharmacist'], section: 'operation' },
  { id: 'compras', label: 'Compras', description: 'Abastecimiento y proveedores', icon: ClipboardList, roles: ['owner', 'pharmacist'], section: 'operation' },
  { id: 'caja', label: 'Caja', description: 'Tu turno y movimientos', icon: Banknote, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation' },
  { id: 'reportes', label: 'Reportes', description: 'Información para decidir', icon: BarChart3, roles: ['owner'], section: 'management' },
  { id: 'empleados', label: 'Empleados', description: 'Tu equipo y sus accesos', icon: UsersRound, roles: ['owner'], section: 'management' },
  { id: 'configuracion', label: 'Configuración', description: 'Tu botica y sucursales', icon: Settings2, roles: ['owner'], section: 'management' },
]
export const availableNavigation = (role: TenantRole) => navigation.filter(item => item.roles.includes(role))
