import { ArrowLeftRight, Banknote, BarChart3, Bell, Boxes, ClipboardList, LayoutDashboard, Package, Settings2, ShoppingBasket, UsersRound } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { TenantRole } from '../features/access'
import type { Availability } from '../shared/ui/availability'
export interface NavigationItem { id: string; label: string; description: string; icon: LucideIcon; roles: TenantRole[]; section: 'operation' | 'management'; availability: Availability }
export const navigation: NavigationItem[] = [
  { id: '', label: 'Inicio', description: 'Tu espacio de trabajo', icon: LayoutDashboard, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation', availability: 'implemented' },
  { id: 'ventas', label: 'Punto de venta', description: 'Búsqueda, carrito, borrador, pagos y ticket interno', icon: ShoppingBasket, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation', availability: 'implemented' },
  { id: 'productos', label: 'Productos', description: 'Catálogo y precios; guardado de presentaciones pendiente de contrato seguro', icon: Package, roles: ['owner'], section: 'operation', availability: 'partial' },
  { id: 'inventario', label: 'Inventario', description: 'Stock, lotes, vencimientos y ajustes autorizados', icon: Boxes, roles: ['owner', 'pharmacist'], section: 'operation', availability: 'implemented' },
  { id: 'compras', label: 'Compras', description: 'Proveedores, borrador y recepción confirmada', icon: ClipboardList, roles: ['owner', 'pharmacist'], section: 'operation', availability: 'implemented' },
  { id: 'caja', label: 'Caja', description: 'Apertura, movimientos, cierre y conciliación', icon: Banknote, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation', availability: 'implemented' },
  { id: 'transferencias', label: 'Transferencias', description: 'Mercadería y sencillo entre sucursales autorizadas', icon: ArrowLeftRight, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation', availability: 'implemented' },
  { id: 'notificaciones', label: 'Notificaciones', description: 'Alertas y lecturas de tu membresía', icon: Bell, roles: ['owner', 'pharmacist', 'cashier'], section: 'operation', availability: 'implemented' },
  { id: 'reportes', label: 'Reportes', description: 'Consultas, exportaciones y comisiones', icon: BarChart3, roles: ['owner'], section: 'management', availability: 'implemented' },
  { id: 'empleados', label: 'Empleados', description: 'Equipo y accesos; incorporación de identidades pendiente', icon: UsersRound, roles: ['owner'], section: 'management', availability: 'partial' },
  { id: 'configuracion', label: 'Configuración', description: 'Licencia, entidades, sucursales, auditoría e importación', icon: Settings2, roles: ['owner'], section: 'management', availability: 'implemented' },
]
export const availableNavigation = (role: TenantRole) => navigation.filter(item => item.roles.includes(role))
