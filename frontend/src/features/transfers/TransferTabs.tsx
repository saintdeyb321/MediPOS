import { Link } from 'react-router'
import { useWorkspace } from '../access'

export function TransferTabs({ cash = false }: { cash?: boolean }) {
  const w = useWorkspace()
  return <nav className="task-tabs transfer-tabs" aria-label="Tipos de transferencia">
    {cash ? <Link className="button quiet" to={w.base + '/transferencias'}>Mercadería</Link> : <span aria-current="page">Mercadería</span>}
    {cash ? <span aria-current="page">Efectivo entre cajas</span>
      : w.access.role !== 'pharmacist' && <Link className="button quiet" to={w.base + '/transferencias/sencillo'}>Efectivo entre cajas</Link>}
  </nav>
}
