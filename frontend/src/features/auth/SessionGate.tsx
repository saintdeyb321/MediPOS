import { Link, Navigate, Outlet } from 'react-router'
import { useAuthState, useSession } from '../../hooks/useSession'
import { ErrorState, Loading } from '../../components/ui'

export function SessionGate() {
  const session = useSession()
  const { expired } = useAuthState()
  if (expired) return <Navigate to="/" replace />
  if (session.isPending) return <Loading label="Verificando tu sesión…" />
  if (session.isError) return <main className="session-recovery"><ErrorState error={session.error} retry={() => void session.refetch()} /><Link className="text-link" to="/">Volver al ingreso</Link></main>
  if (!session.data) return <Navigate to="/" replace />
  return <Outlet />
}
