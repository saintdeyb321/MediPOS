import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query'
import type { Session } from '../types/contracts'
import { AuthContext, RuntimeContext } from './contexts'
import type { Runtime } from './contexts'
function SessionBoundary({ runtime, children }: { runtime: Runtime; children: ReactNode }) {
  const queryClient = useQueryClient()
  const [expired, setExpired] = useState(runtime.initiallyExpired ?? false)
  useEffect(() => runtime.http?.subscribeUnauthorized(() => {
    if (queryClient.getQueryData<Session | null>(['session'])) {
      void queryClient.cancelQueries()
      // Keep the session query's observer attached while invalidating its value.
      // Removing and recreating that query can leave a mounted route observing old data.
      queryClient.removeQueries({ predicate: query => query.queryKey[0] !== 'session' })
      queryClient.setQueryData(['session'], null)
      setExpired(true)
    }
  }), [queryClient, runtime.http])
  return <AuthContext.Provider value={{ expired, reset: () => setExpired(false) }}>{children}</AuthContext.Provider>
}
export function RuntimeProvider({ runtime, children }: { runtime: Runtime; children: ReactNode }) {
  const client = useMemo(() => new QueryClient({ defaultOptions: {
    queries: { retry: false, gcTime: 60_000, refetchOnWindowFocus: 'always' }, mutations: { retry: false },
  } }), [])
  useEffect(() => () => { void client.cancelQueries(); client.clear() }, [client])
  return <RuntimeContext.Provider value={runtime}><QueryClientProvider client={client}>
    <SessionBoundary runtime={runtime}>{children}</SessionBoundary>
  </QueryClientProvider></RuntimeContext.Provider>
}
