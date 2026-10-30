import { Navigate } from 'react-router'
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
  return <main className="entry-page"><Brand />
    {!http ? <Notice tone="error">Se requiere HTTPS y conexión con MediPOS.</Notice> : admission.isError
      ? <PlatformAdmissionError error={admission.error} failedAt={admission.errorUpdatedAt} fetching={admission.isFetching} retry={() => void admission.refetch()} />
      : <LoadingState label="Preparando tu acceso…" />}
    <LogoutButton />
  </main>
}
