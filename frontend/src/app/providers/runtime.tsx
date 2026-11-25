import { useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query'
import type { Session } from '../../features/access'
import { AuthContext, RuntimeContext } from '../../features/access'
import type { Runtime } from '../../features/access'
function SessionBoundary({ runtime, children }: { runtime: Runtime; children: ReactNode }) {
  const queryClient = useQueryClient()
  const [expired, setExpired] = useState(runtime.initiallyExpired ?? false)
  useEffect(() => {
    const identity = (session: Session | null | undefined) => session ? session.userId + '/' + (session.expiresAtUtc ?? '') : null
    let audience = identity(queryClient.getQueryData<Session | null>(['session']))
    return queryClient.getQueryCache().subscribe(event => {
      if (event.type !== 'updated' || event.query.queryKey[0] !== 'session' || event.action.type !== 'success') return
      const next = identity(event.query.state.data as Session | null | undefined)
      if (audience && audience !== next) {
        const predicate = (query: { queryKey: readonly unknown[] }) => query.queryKey[0] !== 'session'
        void queryClient.cancelQueries({ predicate })
        queryClient.removeQueries({ predicate })
      }
      audience = next
    })
  }, [queryClient])
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
