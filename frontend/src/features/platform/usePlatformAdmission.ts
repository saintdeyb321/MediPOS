import { useQuery } from '@tanstack/react-query'
import { useRuntime, useSession } from '../access'
import { platformApi } from './api'

// Entry and console observe one server decision per Google session. This cache
// only chooses a UI; every platform endpoint still authorizes independently.
export function usePlatformAdmission(mode: 'entry' | 'console' | 'cached' = 'entry') {
  const { http } = useRuntime()
  const session = useSession()
  const admission = useQuery({
    queryKey: ['platform-admission', session.data?.userId, session.data?.expiresAtUtc ?? 'current'],
    queryFn: ({ signal }) => platformApi(http!).admission(signal),
    enabled: mode !== 'cached' && Boolean(http && session.data),
    retry: false, retryOnMount: false, refetchOnMount: false,
    staleTime: Infinity, gcTime: 30 * 60_000,
    refetchOnWindowFocus: false, refetchOnReconnect: false,
    refetchInterval: query => mode === 'console' && query.state.status === 'success' ? 60_000 : false,
  })
  return { admission, session, http }
}
