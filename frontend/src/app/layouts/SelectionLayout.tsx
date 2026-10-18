import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { UserRound } from 'lucide-react'
import { Brand } from '../../shared/ui/Brand'
import { useRuntime } from '../../features/access'
import { LogoutButton } from '../../features/auth'
import './selection.css'

export function SelectionLayout({ children }: { children: ReactNode }) {
  const { preview } = useRuntime()
  return (
    <div className="selection-page">
      <a href="#contenido" className="skip-link">Saltar al contenido</a>
      <header className="selection-header">
        <Link to="/seleccionar-botica" aria-label="MediPOS, elegir botica"><Brand /></Link>
        <div className="selection-account">
          <Link to="/plataforma">Plataforma</Link>
          <span><UserRound size={18} aria-hidden="true" />{preview ? 'Sesión de ejemplo' : 'Mi cuenta'}</span>
          <LogoutButton />
        </div>
      </header>
      <main id="contenido" tabIndex={-1} className="selection-content">{children}</main>
      <footer className="selection-footer"><span>Tu botica, en orden.</span><span>MediPOS · Perú</span></footer>
    </div>
  )
}
