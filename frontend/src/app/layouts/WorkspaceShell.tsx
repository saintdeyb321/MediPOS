import { WorkspaceOutboxSync } from '../../features/offline'
import { useEffect, useRef } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { X } from 'lucide-react'
import type { Workspace } from '../../features/access'
import { IconButton } from '../../shared/ui/Button'
import { Dialog } from '../../shared/ui/Dialog'
import { Breadcrumbs } from '../../shared/ui/Breadcrumbs'
import { availableNavigation } from '../navigation'
import { WorkspaceNavigation } from './WorkspaceNavigation'
import { WorkspaceHeader } from './WorkspaceHeader'
import './workspace.css'

export function WorkspaceShell({ workspace }: { workspace: Workspace }) {
  const mobile = useRef<HTMLDialogElement>(null)
  const navigate = useNavigate()
  const { pathname } = useLocation(); const content = useRef<HTMLElement>(null)
  const relative = pathname.slice(workspace.base.length).replace(/^\//, '')
  const submodules: Record<string, { label: string; parent: string }> = {
    'transferencias/sencillo': { label: 'Transferencia de sencillo', parent: 'transferencias' },
    comisiones: { label: 'Reglas de comisiones', parent: 'reportes' },
    auditoria: { label: 'Historial de auditoría', parent: 'configuracion' },
    importacion: { label: 'Importar productos', parent: 'productos' },
  }
  const submodule = submodules[relative]
  const module = availableNavigation(workspace.access.role).find(item => item.id === (submodule?.parent ?? relative))
  const parentTo = submodule ? `${workspace.base}/${submodule.parent}` : workspace.base
  const crumbs = [{ label: 'Inicio', to: relative ? workspace.base : undefined }, ...(relative ? [{ label: module?.label ?? 'Módulo', to: submodule ? parentTo : undefined }] : []), ...(submodule ? [{ label: submodule.label }] : [])]
  useEffect(() => { content.current?.focus({ preventScroll: true }) }, [pathname])
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
    <div className={`app-shell${relative === 'ventas' ? ' app-shell-pos' : ''}`}>
      <a href="#contenido" className="skip-link">Saltar al contenido</a>
      <aside className="desktop-sidebar"><WorkspaceNavigation workspace={workspace} /></aside>
      <Dialog dialogRef={mobile} label="Menú de MediPOS" className="mobile-navigation">
        <IconButton className="close-nav" label="Cerrar navegación" onClick={closeMenu}><X /></IconButton>
        <WorkspaceNavigation workspace={workspace} onNavigate={closeMenu} />
      </Dialog>
      <div className="workspace">
        <WorkspaceHeader workspace={workspace} openMenu={() => mobile.current?.showModal()} />
        <WorkspaceOutboxSync /><main ref={content} id="contenido" tabIndex={-1} className="workspace-main">{submodule && <Breadcrumbs items={crumbs.slice(1)} />}<Outlet /></main>
        {relative !== 'ventas' && <footer className="workspace-footer"><span>MediPOS · Perú</span></footer>}
      </div>
    </div>
  )
}
