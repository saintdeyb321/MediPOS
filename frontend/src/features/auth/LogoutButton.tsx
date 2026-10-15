import type { LucideIcon } from 'lucide-react'
import { LogOut } from 'lucide-react'
import { errorMessage } from '../access'
import { Button } from '../../shared/ui/Button'
import { useLogout } from './useLogout'

export function LogoutButton({ icon: Icon = LogOut }: { icon?: LucideIcon }) {
  const logout = useLogout()
  return (
    <span className="logout-container">
      <Button variant="quiet" className="logout-button" pending={logout.isPending} onClick={() => logout.mutate()}>
        <Icon size={18} aria-hidden="true" /><span>{logout.isPending ? 'Cerrando…' : 'Cerrar sesión'}</span>
      </Button>
      {logout.isError && <span className="logout-error" role="alert">{errorMessage(logout.error)}</span>}
    </span>
  )
}
