import type { ReactNode } from 'react'
import { ShoppingBasket } from 'lucide-react'

export function PosHeader({ status }: { status?: ReactNode }) {
  return <div className="pos-heading"><h1><ShoppingBasket size={22} aria-hidden="true" />Punto de venta</h1><div className="cash-status-strip">{status}</div></div>
}
