import { useEffect, useRef } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Navigate } from 'react-router'
import { ArrowRight, Check, LockKeyhole, ShieldCheck, Store, PackageCheck, HeartPulse } from 'lucide-react'
import { Brand } from '../../components/ui'
import { useAuthState, useRuntime, useSession } from '../../hooks/useSession'
import { errorMessage } from '../../services/errors'

export function LoginPage({ onPreview }: { onPreview?: () => void }) {
  const { service, preview } = useRuntime()
  const { expired } = useAuthState()
  const session = useSession()
  const loginRequest = useRef<AbortController | null>(null)
  useEffect(() => () => loginRequest.current?.abort(), [])
  const login = useMutation({ mutationFn: () => {
    loginRequest.current?.abort()
    loginRequest.current = new AbortController()
    return service.login(loginRequest.current.signal)
  } })
  if (session.data && !expired) return <Navigate to="/seleccionar-botica" replace />
  const checkingSession = !expired && session.isPending
  const busy = checkingSession || login.isPending || login.isSuccess
  return <main className="login-page">
    <section className="login-story" aria-label="Bienvenido a MediPOS">
      <Brand />
      <div className="story-copy"><span className="story-label"><span className="status-dot" /> HECHO PARA TU BOTICA</span>
        <h1>Más cerca de tus clientes.<br /><em>Al día con tu negocio.</em></h1>
        <p>Todo lo que tu botica necesita, en un solo lugar. Menos tareas, más tiempo para una buena atención.</p>
        <div className="pharmacy-illustration" aria-hidden="true"><div className="illustration-orbit" /><div className="illustration-window"><div className="illustration-bar"><i /><i /><i /></div><div className="illustration-shop"><span className="shop-cross">+</span><div className="shop-awning"><i /><i /><i /><i /><i /></div><div className="shop-body"><span /><span /><span /><div className="shop-door" /></div></div><div className="illustration-caption"><HeartPulse size={21} /><span>Tu botica, en orden.</span><Check size={18} /></div></div><span className="floating-icon inventory"><PackageCheck size={25} /></span><span className="floating-icon secure"><ShieldCheck size={25} /></span></div>
        <div className="story-features"><span><Check size={15} /> Ventas ágiles</span><span><Check size={15} /> Stock bajo control</span><span><Check size={15} /> Información clara</span></div>
      </div><p className="story-footer">Diseñado para boticas y farmacias del Perú.</p>
    </section>
    <section className="login-form-area"><div className="mobile-brand"><Brand /></div><div className="login-form">
      <span className="welcome-icon"><Store size={28} /></span><p className="eyebrow">BIENVENIDO A MEDIPOS</p><h2>Un buen día empieza aquí.</h2><p className="login-description">Ingresa con tu cuenta de Google para acceder a tu botica.</p>
      {expired && <div className="notice warning" role="alert"><LockKeyhole size={20} /><span>Tu sesión terminó. Ingresa de nuevo para continuar de forma segura.</span></div>}
      {(session.isError || login.isError) && <div className="notice error" role="alert"><span>{errorMessage(login.error ?? session.error)}</span></div>}
      <button className="google-button" disabled={busy || preview} onClick={() => login.mutate()}>
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24"><path fill="#4285F4" d="M21.8 12.2c0-.7-.1-1.4-.2-2.2H12v4.2h5.5a4.7 4.7 0 0 1-2.1 3.1V20h3.4c2-1.8 3-4.5 3-7.8"/><path fill="#34A853" d="M12 22c2.7 0 5-.9 6.8-2.5l-3.4-2.6c-.9.6-2 .9-3.4.9-2.6 0-4.8-1.8-5.6-4.1H2.9v2.7A10 10 0 0 0 12 22"/><path fill="#FBBC05" d="M6.4 13.7a6 6 0 0 1 0-3.4V7.6H2.9a10 10 0 0 0 0 8.8z"/><path fill="#EA4335" d="M12 6.2c1.5 0 2.8.5 3.8 1.5L18.7 5A9.6 9.6 0 0 0 12 2a10 10 0 0 0-9.1 5.6l3.5 2.7A5.9 5.9 0 0 1 12 6.2"/></svg>
        {checkingSession ? 'Verificando sesión…' : busy ? 'Abriendo Google…' : 'Continuar con Google'}<ArrowRight size={17} />
      </button>
      {session.isError && <button className="text-link retry-link" onClick={() => void session.refetch()}>Revisar conexión</button>}
      <p className="login-security"><ShieldCheck size={17} /> Tu cuenta, protegida con Google.<br />MediPOS nunca solicita ni almacena tu contraseña.</p>
      <div className="login-help"><h3>¿Aún no tienes acceso?</h3><p>Solicita al propietario que asigne tu cuenta a una botica y sucursal.</p></div>
      {import.meta.env.DEV && onPreview && <button className="preview-link" disabled={login.isPending || login.isSuccess} onClick={onPreview}>Explorar vista de desarrollo <ArrowRight size={15} /><span>Datos sintéticos · sin autenticación real</span></button>}
    </div><footer className="login-footer"><span>© {new Date().getFullYear()} MediPOS</span><span><LockKeyhole size={12} /> Acceso seguro</span></footer></section>
  </main>
}
