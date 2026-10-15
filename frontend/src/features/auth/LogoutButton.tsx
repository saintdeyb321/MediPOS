import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import type { LucideIcon } from 'lucide-react'
import { LogOut } from 'lucide-react'
import { useAuthState, useRuntime } from '../../hooks/useSession'
import { errorMessage } from '../../services/errors'
export function LogoutButton({ icon: Icon = LogOut }: { icon?: LucideIcon }) {
  const { service } = useRuntime(); const client = useQueryClient(); const navigate = useNavigate(); const auth = useAuthState()
  const logout = useMutation({ mutationFn: () => service.logout(), onSuccess: async () => {
    await client.cancelQueries(); client.clear(); client.setQueryData(['session'], null); auth.reset(); navigate('/', { replace: true })
  } })
  return <span className="logout-container"><button className="logout-button" disabled={logout.isPending} onClick={() => logout.mutate()}><Icon size={17} /><span>{logout.isPending ? 'Cerrando…' : 'Cerrar sesión'}</span></button>
    {logout.isError && <span className="logout-error" role="alert">{errorMessage(logout.error)}</span>}</span>
}
