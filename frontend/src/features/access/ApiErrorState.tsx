import { ApiError } from '../../shared/api/http'
import { ErrorState } from '../../shared/ui/States'
import { errorMessage } from './errors'

export function ApiErrorState({ error, retry, title }: { error: unknown; retry?: () => void; title?: string }) {
  return <ErrorState message={errorMessage(error) ?? 'No pudimos completar la solicitud.'} reference={error instanceof ApiError ? error.traceId : undefined} retry={retry} title={title} />
}
