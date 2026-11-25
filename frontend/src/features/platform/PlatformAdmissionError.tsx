import { cooldownDeadline, usePlatformCooldown } from './usePlatformCooldown'
import { ApiErrorState } from '../access'
import { ApiError } from '../../shared/api/http'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'

export function PlatformAdmissionError({ error, failedAt, fetching, retry, cached }: { error: unknown; failedAt: number; fetching: boolean; retry: () => void; cached?: boolean }) {
  if (!(error instanceof ApiError) || error.status !== 429) return <>
    {error instanceof ApiError && error.status === 403 && error.code === 'access.platform_denied'
      ? <Notice tone="warning">Esta cuenta no tiene autoridad de plataforma. Ser Owner no concede este acceso.</Notice>
      : <Notice tone="warning">No pudimos verificar tu acceso. Reconsulta para volver a la consola.</Notice>}
    <ApiErrorState error={error} retry={retry} />
  </>
  return <>
    <Notice tone="warning">{cached ? 'Puedes consultar datos de la última lectura. Para actualizar o guardar, vuelve a verificar el acceso.' : 'Verifica el acceso para abrir la consola. Las solicitudes pendientes se conservan.'}</Notice>
    <PlatformReadError error={error} failedAt={failedAt} fetching={fetching} retry={retry} label="Reconsultar acceso" />
  </>
}

export function PlatformReadError({ error, failedAt, fetching, retry, label = 'Reconsultar' }: { error: unknown; failedAt: number; fetching: boolean; retry?: () => void; label?: string }) {
  const remaining = usePlatformCooldown(cooldownDeadline(error, failedAt), failedAt)
  if (error == null) return null
  if (!(error instanceof ApiError) || error.status !== 429) return <ApiErrorState error={error} retry={retry} />
  return <div className="platform-read-error">
    <ApiErrorState error={error} title="Pausa por límite administrativo" />
    {remaining > 0 && <p>Podrás reconsultar en {remaining} segundos.</p>}
    {retry && <Button variant="secondary" disabled={remaining > 0 || fetching} onClick={retry}>{label}</Button>}
  </div>
}
