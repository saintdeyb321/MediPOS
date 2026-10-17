import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ApiErrorState, useRuntime, useSession } from '../access'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { Panel } from '../../shared/ui/Panel'
import { invitationsApi } from './invitations.api'

export function AcceptInvitationPage() {
  const session = useSession(); const [ticket] = useState(() => window.location.hash.length <= 4097 ? window.location.hash.slice(1) : '')
  useEffect(() => {
    const fragment = window.location.hash.slice(1)
    if (fragment) {
      window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search)
    }
  }, [])
  return <main className="state-block"><Panel><h1>Aceptar invitación de empleado</h1>
    <p>La aceptación vincula tu cuenta Google actual para revisión. No concede membresía ni permisos hasta que el propietario emisor verifique y apruebe tu cuenta.</p>
    {session.isPending ? <p role="status">Verificando sesión…</p> : session.isError ? <ApiErrorState error={session.error} title="No pudimos verificar tu sesión" /> : session.data ?
      <AcceptanceForm key={session.data.userId} ticket={ticket} /> : <><p>Primero inicia sesión con tu cuenta real. Después vuelve a abrir el enlace original: no guardamos el secreto durante el login ni lo enviamos como dirección de retorno.</p><Link to="/">Ir al inicio e ingresar con Google</Link></>}
    <Link to="/seleccionar-botica">Ir a mis boticas</Link>
  </Panel></main>
}
function AcceptanceForm({ ticket }: { ticket: string }) {
  const { http } = useRuntime(); const [reviewed, setReviewed] = useState(false); const [busy, setBusy] = useState(false)
  const [accepted, setAccepted] = useState(false); const [error, setError] = useState<unknown>(null)
  const running = useRef(false); const controller = useRef<AbortController | null>(null); const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; controller.current?.abort() } }, [])
  async function accept() {
    if (!http || !ticket || !reviewed || busy || running.current || accepted) return
    running.current = true; setBusy(true); setError(null); controller.current = new AbortController()
    try { await invitationsApi(http).accept(ticket, controller.current.signal); if (mounted.current) setAccepted(true) }
    catch (failure) { if (mounted.current) setError(failure) }
    finally { running.current = false; if (mounted.current) setBusy(false) }
  }
  if (accepted) return <p role="status">Aceptación registrada. El propietario debe revisar y aprobar tu cuenta antes de que aparezca la botica. No se creó una membresía por aceptar el enlace.</p>
  return <form aria-label="Aceptar vínculo de cuenta Google" onSubmit={event => { event.preventDefault(); void accept() }}>
    {!ticket && <p>Vuelve a abrir el enlace original después del login. No conservamos el secreto al recargar esta página.</p>}
    {error != null && <ApiErrorState error={error} title="No pudimos confirmar la aceptación" />}
    {error instanceof ApiError && error.outcomeUnknown && <p>No se creará otra invitación. Puedes comprobar la misma aceptación con esta misma sesión: el servidor solo permite vincular una cuenta y no repite membresías.</p>}
    <label><input type="checkbox" checked={reviewed} disabled={busy || !ticket} onChange={event => setReviewed(event.target.checked)} /> Confirmo que esta es mi sesión Google y acepto que el propietario revise la cuenta que estoy vinculando.</label>
    <Button type="submit" disabled={!http || !ticket || !reviewed || busy}>{error instanceof ApiError && error.outcomeUnknown ? 'Comprobar la misma aceptación' : 'Aceptar invitación'}</Button>
  </form>
}
