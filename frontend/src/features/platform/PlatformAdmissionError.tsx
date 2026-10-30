import { useEffect, useState } from 'react'
import { ApiErrorState } from '../access'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'

export function PlatformAdmissionError({ error, failedAt, fetching, retry }: { error: unknown; failedAt: number; fetching: boolean; retry: () => void }) {
  const limited = error instanceof ApiError && error.status === 429
  const [now, setNow] = useState(() => Date.now())
  // No Retry-After is currently returned by this endpoint. Wait a complete server
  // window from the rejection; only the operator may request another admission.
  const deadline = failedAt + 60_000
  const remaining = Math.max(0, Math.ceil((deadline - now) / 1000))
  useEffect(() => {
    if (!limited || Date.now() >= deadline) return
    const timer = window.setInterval(() => {
      setNow(Date.now())
      if (Date.now() >= deadline) window.clearInterval(timer)
    }, 1000)
    return () => window.clearInterval(timer)
  }, [limited, deadline])
  if (!limited) return <>
    {error instanceof ApiError && error.status === 403 && error.code === 'access.platform_denied'
      ? <Notice tone="warning">Esta cuenta no tiene autoridad de plataforma. Ser Owner no concede este acceso.</Notice>
      : <Notice tone="warning">No pudimos verificar tu acceso. Un problema de conexión o del servicio no cambia los permisos de tu cuenta.</Notice>}
    <ApiErrorState error={error} retry={retry} />
  </>
  return <>
    <ApiErrorState error={error} title="Pausa por límite administrativo" />
    <Notice>La consola permanece bloqueada hasta verificar el acceso. Las solicitudes pendientes se conservan; no se reenviará ninguna operación.</Notice>
    <p role="status">{remaining > 0 ? `Podrás reconsultar el acceso en ${remaining} segundos.` : 'Ya puedes solicitar una nueva comprobación de acceso.'}</p>
    <Button variant="secondary" disabled={remaining > 0 || fetching} onClick={retry}>Reconsultar acceso</Button>
  </>
}
