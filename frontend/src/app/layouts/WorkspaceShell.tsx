import { useEffect, useRef } from 'react'
import { Outlet, useNavigate } from 'react-router'
import { X } from 'lucide-react'
import type { Workspace } from '../../features/access'
import { IconButton } from '../../shared/ui/Button'
import { Dialog } from '../../shared/ui/Dialog'
import { WorkspaceNavigation } from './WorkspaceNavigation'
import { WorkspaceHeader } from './WorkspaceHeader'
import './workspace.css'

export function WorkspaceShell({ workspace }: { workspace: Workspace }) {
  const mobile = useRef<HTMLDialogElement>(null)
  const navigate = useNavigate()
  useEffect(() => {
    const shortcut = (event: KeyboardEvent) => {
      const target = event.target
      const editing = target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
      if (event.altKey && event.key.toLowerCase() === 'v' && !editing) {
        event.preventDefault()
        if (mobile.current?.open) mobile.current.close()
        navigate(`${workspace.base}/ventas`)
      }
    }
    window.addEventListener('keydown', shortcut)
    return () => window.removeEventListener('keydown', shortcut)
  }, [navigate, workspace.base])
  function closeMenu() {
    if (mobile.current?.open) mobile.current.close()
  }
  return (
    <div className="app-shell">
      <a href="#contenido" className="skip-link">Saltar al contenido</a>
      <aside className="desktop-sidebar"><WorkspaceNavigation workspace={workspace} /></aside>
      <Dialog dialogRef={mobile} label="Menú de MediPOS" className="mobile-navigation">
        <IconButton className="close-nav" label="Cerrar navegación" onClick={closeMenu}><X /></IconButton>
        <WorkspaceNavigation workspace={workspace} onNavigate={closeMenu} />
      </Dialog>
      <div className="workspace">
        <WorkspaceHeader workspace={workspace} openMenu={() => mobile.current?.showModal()} />
        <main id="contenido" tabIndex={-1} className="workspace-main"><Outlet /></main>
        <footer className="workspace-footer"><span>{workspace.tenantName} · {workspace.branch.name}</span><span>MediPOS · Perú</span></footer>
      </div>
    </div>
  )
}
