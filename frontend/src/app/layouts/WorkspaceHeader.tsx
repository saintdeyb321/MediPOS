import { Menu } from 'lucide-react'
import type { Workspace } from '../../features/access'
import { IconButton } from '../../shared/ui/Button'

export function WorkspaceHeader({ workspace: w, openMenu }: { workspace: Workspace; openMenu: () => void }) {
  return (
    <header className="workspace-header">
      <div className="header-location">
        <IconButton className="mobile-menu-toggle" label="Abrir navegación" aria-haspopup="dialog" onClick={openMenu}><Menu /></IconButton>
        <span className="header-context">{w.tenantName}<span aria-hidden="true"> / </span><strong>{w.branch.name}</strong></span>
      </div>
    </header>
  )
}
