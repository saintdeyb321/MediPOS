import { Link, NavLink } from 'react-router'
import { ArrowRightLeft, ShieldCheck, Store } from 'lucide-react'
import { availableNavigation } from '../navigation'
import type { Workspace } from '../../features/access'
import { useRuntime } from '../../features/access'
import { Brand } from '../../shared/ui/Brand'
import { NotificationCounter } from '../../features/notifications'

export function WorkspaceNavigation({ workspace: w, onNavigate }: { workspace: Workspace; onNavigate?: () => void }) {
  const { preview } = useRuntime()
  const links = availableNavigation(w.access.role)
  return (
    <>
      <div className="sidebar-brand"><Link to={w.base} aria-label="MediPOS, inicio" onClick={onNavigate}><Brand /></Link></div>
      <div className="sidebar-tenant"><Store size={22} aria-hidden="true" /><div><strong>{w.tenantName}</strong><span>Espacio de trabajo</span></div></div>
      <nav aria-label="Navegación principal">
        {(['operation', 'management'] as const).map(section => {
          const items = links.filter(item => item.section === section)
          return items.length > 0 && (
            <div className="nav-section" key={section}>
              <p>{section === 'operation' ? 'TU BOTICA' : 'GESTIÓN'}</p>
              {items.map(item => (
                <NavLink key={item.id} to={item.id ? `${w.base}/${item.id}` : w.base} end className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`} onClick={onNavigate}
                  title={item.id && !['ventas', 'configuracion', 'transferencias', 'reportes', 'notificaciones'].includes(item.id) ? 'Próximamente: esta sección aún no permite operaciones.' : undefined}>
                  <item.icon size={20} aria-hidden="true" />
                  <span>{item.label}</span>
                  {item.id === 'notificaciones' && <NotificationCounter />}
                  {item.id === 'ventas' ? <kbd aria-hidden="true">Alt V</kbd> : item.id && !['configuracion', 'transferencias', 'reportes', 'notificaciones'].includes(item.id) && <small aria-hidden="true">Pronto</small>}
                </NavLink>
              ))}
            </div>
          )
        })}
      </nav>
      <div className="sidebar-bottom">
        <span className="sidebar-status"><ShieldCheck size={18} aria-hidden="true" />{preview ? 'Acceso de ejemplo' : 'Acceso verificado'}</span>
        <p>{preview ? 'Datos sintéticos · sin sesión real.' : 'La operación se valida en el servidor.'}</p>
        <Link to="/seleccionar-botica" className="switch-tenant" onClick={onNavigate}><ArrowRightLeft size={18} aria-hidden="true" />Cambiar botica</Link>
      </div>
    </>
  )
}
