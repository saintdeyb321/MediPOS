import { Link, Navigate } from 'react-router'
import { ApiError } from '../../shared/api/http'
import { Brand } from '../../shared/ui/Brand'
import { Notice } from '../../shared/ui/Notice'
import { LoadingState } from '../../shared/ui/States'
import { LogoutButton } from '../../features/auth'
import { PlatformAdmissionError, usePlatformAdmission } from '../../features/platform'

export function EntryRouter() {
  const { admission, http } = usePlatformAdmission()
  if (admission.isSuccess) return <Navigate to="/plataforma" replace />
  if (admission.error instanceof ApiError && admission.error.status === 403 && admission.error.code === 'access.platform_denied')
    return <Navigate to="/seleccionar-botica" replace />
  const unavailable = admission.error instanceof ApiError && admission.error.status === 404
  return <main className="entry-page"><Brand />
    {!http ? <Notice tone="error">Se requiere HTTPS y conexión con MediPOS.</Notice> : admission.isError
      ? <PlatformAdmissionError error={admission.error} failedAt={admission.errorUpdatedAt} fetching={admission.isFetching} retry={() => void admission.refetch()} />
      : <LoadingState label="Preparando tu acceso…" />}
    {unavailable && <>
      <Notice>La ruta de plataforma no está disponible: puede estar deshabilitada o existir un problema de configuración. Esto no confirma permisos administrativos. Puedes consultar tus boticas; el servidor comprobará por separado tu acceso comercial.</Notice>
      <Link className="button secondary" to="/seleccionar-botica">Continuar a mis boticas</Link>
    </>}
    <LogoutButton />
  </main>
}
