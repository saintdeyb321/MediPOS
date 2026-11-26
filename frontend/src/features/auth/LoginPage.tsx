import { Navigate } from 'react-router'
import { Brand } from '../../shared/ui/Brand'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { errorMessage } from '../access'
import { GoogleMark } from './GoogleMark'
import { useLogin } from './useLogin'
import './auth.css'

export function LoginPage() {
  const { preview, expired, session, login } = useLogin()
  if (session.data && !expired) return <Navigate to="/entrada" replace />
  const checking = !expired && session.isPending
  const busy = checking || login.isPending || login.isSuccess
  return <main className="login-page">
    <section className="login-form" aria-labelledby="login-title">
      <header className="login-banner"><Brand /></header>
      <div className="login-fields">
      <div className="login-copy">
        <h1 id="login-title">Ingresa a MediPOS</h1>
        <p>Usa la cuenta de Google asociada a tu botica.</p>
      </div>
      {expired && <Notice tone="warning">Tu sesión terminó. Ingresa de nuevo para continuar de forma segura.</Notice>}
      {(session.isError || login.isError) && <Notice tone="error">{errorMessage(login.error ?? session.error)}</Notice>}
      <Button className="google-button" variant="secondary" disabled={busy || preview} pending={busy} onClick={() => login.mutate()}>
        <GoogleMark />{checking ? 'Verificando sesión…' : busy ? 'Abriendo Google…' : 'Continuar con Google'}
      </Button>
      {session.isError && <Button variant="quiet" onClick={() => void session.refetch()}>Revisar conexión</Button>}
      <p className="login-help">Si recibiste una invitación, ingresa con el correo que figura en ella.</p>
      </div>
    </section>
  </main>
}
