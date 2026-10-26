import { Link, NavLink } from 'react-router'
import { ArrowRightLeft, ChevronDown, MapPin, Store, UserRound } from 'lucide-react'
import { availableNavigation } from '../navigation'
import type { Workspace } from '../../features/access'
import { roleLabels } from '../../features/access'
import { LogoutButton } from '../../features/auth'
import { Brand } from '../../shared/ui/Brand'
import { NotificationCounter } from '../../features/notifications'
import { availabilityLabels } from '../../shared/ui/availability'

export function WorkspaceNavigation({ workspace: w, onNavigate }: { workspace: Workspace; onNavigate?: () => void }) {
  const links = availableNavigation(w.access.role)
  return (
    <>
      <div className="sidebar-brand"><Link to={w.base} aria-label="MediPOS, inicio" onClick={onNavigate}><Brand /></Link></div>
      <div className="sidebar-tenant"><Store size={20} aria-hidden="true" /><div><strong>{w.tenantName}</strong></div></div>
      <Link className="sidebar-branch" to={`/boticas/${w.tenantId}/sucursales`} onClick={onNavigate}><MapPin size={18} aria-hidden="true" /><span><small>Sede actual</small><strong>{w.branch.name}</strong></span><ChevronDown size={16} aria-hidden="true" /></Link>
      <nav aria-label="Navegación principal" className="sidebar-links">
        {(['operation', 'management', 'more'] as const).map(section => {
          const items = links.filter(item => section === 'more' ? ['caja', 'sincronizacion'].includes(item.id) : item.section === section && !['caja', 'sincronizacion'].includes(item.id))
          return items.length > 0 && (
            <div className="nav-section" key={section}>
              <p>{section === 'operation' ? 'OPERACIÓN' : section === 'management' ? 'GESTIÓN' : 'MÁS'}</p>
              {items.map(item => (
                <NavLink key={item.id} to={item.id ? `${w.base}/${item.id}` : w.base} end className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`} onClick={onNavigate}
                  aria-label={item.availability !== 'implemented' ? `${item.label} · ${availabilityLabels[item.availability]}` : undefined}
                  title={item.availability !== 'implemented' ? item.description : undefined}>
                  <item.icon size={20} aria-hidden="true" />
                  <span>{item.id === 'caja' ? 'Arqueos y cajas' : item.label}</span>
                  {item.id === 'notificaciones' && <NotificationCounter />}
                  {item.id === 'ventas' ? <kbd aria-hidden="true">Alt V</kbd> : item.availability !== 'implemented' && <small aria-hidden="true">{availabilityLabels[item.availability]}</small>}
                </NavLink>
              ))}
            </div>
          )
        })}
      </nav>
      <div className="sidebar-bottom">
        <details className="sidebar-account"><summary><UserRound size={20} aria-hidden="true" /><span>{roleLabels[w.access.role]}<small>Mi cuenta</small></span><ChevronDown size={16} aria-hidden="true" /></summary><div><p>Cuenta autenticada en esta botica.</p><details className="inline-help"><summary>Identificador para soporte</summary><code>{w.session.userId}</code></details></div></details>
        <Link to="/seleccionar-botica" className="switch-tenant" onClick={onNavigate}><ArrowRightLeft size={18} aria-hidden="true" />Cambiar botica</Link>
        <LogoutButton />
      </div>
    </>
  )
}
