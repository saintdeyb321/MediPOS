import { ChevronDown, MapPin, Menu, UserRound } from 'lucide-react'
import { Link, useLocation } from 'react-router'
import { availableNavigation } from '../navigation'
import { roleLabels } from '../../features/access'
import type { Workspace } from '../../features/access'
import { LogoutButton } from '../../features/auth'
import { IconButton } from '../../shared/ui/Button'

export function WorkspaceHeader({ workspace: w, openMenu }: { workspace: Workspace; openMenu: () => void }) {
  const { pathname } = useLocation()
  const page = availableNavigation(w.access.role).find(item => (item.id ? `${w.base}/${item.id}` : w.base) === pathname)
  return (
    <header className="workspace-header">
      <div className="header-location">
        <IconButton className="mobile-menu-toggle" label="Abrir navegación" aria-haspopup="dialog" onClick={openMenu}><Menu /></IconButton>
        <span className="header-page">{page?.label ?? 'Tu botica'}</span>
        <Link className="branch-switch" to={`/boticas/${w.tenantId}/sucursales`}>
          <MapPin size={18} aria-hidden="true" /><span><small>{w.tenantName}</small>{w.branch.name}</span><ChevronDown size={16} aria-hidden="true" />
        </Link>
      </div>
      <details className="account-menu" onKeyDown={event => { if (event.key === 'Escape') { event.currentTarget.open = false; event.currentTarget.querySelector('summary')?.focus() } }}>
        <summary aria-label="Mi cuenta">
          <span className="user-avatar"><UserRound size={20} aria-hidden="true" /></span>
          <span className="account-label">{roleLabels[w.access.role]}<small>Mi cuenta</small></span>
          <ChevronDown size={16} aria-hidden="true" />
        </summary>
        <div className="account-dropdown">
          <strong>Mi cuenta</strong><p>{roleLabels[w.access.role]} · {w.tenantName}</p>
          <details className="inline-help"><summary>Detalles para soporte</summary><p>Identificador de cuenta</p><code>{w.session.userId}</code></details><LogoutButton />
        </div>
      </details>
    </header>
  )
}
