import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { UserRound } from 'lucide-react'
import { Brand } from '../../shared/ui/Brand'
import { useRuntime } from '../../features/access'
import { LogoutButton } from '../../features/auth'
import { usePlatformAdmission } from '../../features/platform'
import './selection.css'

export function SelectionLayout({ children }: { children: ReactNode }) {
  const { preview } = useRuntime()
  const { pathname } = useLocation()
  const main = useRef<HTMLElement>(null)
  useEffect(() => { main.current?.focus({ preventScroll: true }) }, [pathname])
  const { admission } = usePlatformAdmission('cached')
  return (
    <div className="selection-page">
      <a href="#contenido" className="skip-link">Saltar al contenido</a>
      <header className="selection-header">
        <Link to="/seleccionar-botica?elegir=1" aria-label="MediPOS, elegir botica"><Brand /></Link>
        <div className="selection-account">
          {admission.isSuccess && <Link to="/plataforma">Cambiar a Plataforma</Link>}
          <span className="selection-account-name"><UserRound size={18} aria-hidden="true" />{preview ? 'Sesión de ejemplo' : 'Mi cuenta'}</span>
          <LogoutButton />
        </div>
      </header>
      <main ref={main} id="contenido" tabIndex={-1} className="selection-content">{children}</main>
    </div>
  )
}
