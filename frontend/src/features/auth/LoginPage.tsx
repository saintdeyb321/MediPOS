import { Navigate } from 'react-router'
import { ArrowRight, Boxes, LockKeyhole, ShieldCheck, ShoppingBasket, Store } from 'lucide-react'
import { Brand } from '../../shared/ui/Brand'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { errorMessage } from '../access'
import { GoogleMark } from './GoogleMark'
import { useLogin } from './useLogin'
import './auth.css'

export function LoginPage({ onPreview }: { onPreview?: () => void }) {
  const { preview, expired, session, login } = useLogin()
  if (session.data && !expired) return <Navigate to="/seleccionar-botica" replace />
  const checkingSession = !expired && session.isPending
  const busy = checkingSession || login.isPending || login.isSuccess
  return (
    <main className="login-page">
      <section className="login-story" aria-label="Bienvenido a MediPOS">
        <Brand />
        <div className="story-copy">
          <p className="eyebrow">HECHO PARA TU BOTICA</p>
          <h1>Más cerca de tus clientes.<br /><em>Al día con tu negocio.</em></h1>
          <p>Un espacio claro para el trabajo diario de tu botica.</p>
          <ul className="story-benefits">
            <li><ShoppingBasket aria-hidden="true" /><div><strong>Atención ágil</strong><span>Un punto de venta pensado para el mostrador.</span></div></li>
            <li><Boxes aria-hidden="true" /><div><strong>Cada sucursal, en orden</strong><span>El contexto de trabajo siempre a la vista.</span></div></li>
            <li><ShieldCheck aria-hidden="true" /><div><strong>Acceso personal</strong><span>Tu cuenta y las sucursales autorizadas.</span></div></li>
          </ul>
        </div>
        <p className="story-footer">Diseñado para boticas y farmacias del Perú.</p>
      </section>
      <section className="login-form-area">
        <div className="mobile-brand"><Brand /></div>
        <div className="login-form">
          <span className="state-icon"><Store size={28} aria-hidden="true" /></span>
          <p className="eyebrow">BIENVENIDO A MEDIPOS</p>
          <h2>Un buen día empieza aquí.</h2>
          <p className="login-description">Ingresa con tu cuenta de Google para acceder a tu botica.</p>
          {expired && <Notice tone="warning">Tu sesión terminó. Ingresa de nuevo para continuar de forma segura.</Notice>}
          {(session.isError || login.isError) && <Notice tone="error">{errorMessage(login.error ?? session.error)}</Notice>}
          <Button className="google-button" variant="secondary" disabled={busy || preview} pending={busy} onClick={() => login.mutate()}>
            <GoogleMark />{checkingSession ? 'Verificando sesión…' : busy ? 'Abriendo Google…' : 'Continuar con Google'}<ArrowRight size={18} aria-hidden="true" />
          </Button>
          {session.isError && <Button variant="quiet" onClick={() => void session.refetch()}>Revisar conexión</Button>}
          <p className="login-security"><ShieldCheck size={20} aria-hidden="true" />MediPOS nunca solicita ni almacena tu contraseña de Google.</p>
          <div className="login-help"><h3>¿Aún no tienes acceso?</h3><p>Solicita al propietario que asigne tu cuenta a una botica y sucursal.</p></div>
          {import.meta.env.DEV && onPreview && (
            <Button variant="quiet" className="preview-link" disabled={login.isPending || login.isSuccess} onClick={onPreview}>
              Explorar vista de desarrollo <ArrowRight size={16} aria-hidden="true" /><span>Datos sintéticos · sin autenticación real</span>
            </Button>
          )}
        </div>
        <footer className="login-footer"><span>© {new Date().getFullYear()} MediPOS</span><span><LockKeyhole size={16} aria-hidden="true" />Acceso seguro</span></footer>
      </section>
    </main>
  )
}
