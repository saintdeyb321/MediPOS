import { ShoppingBasket } from 'lucide-react'
import { Panel } from '../../shared/ui/Panel'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/States'
import { CheckoutSummary } from './CheckoutSummary'

export function SaleCartPanel() {
  return (
    <Panel id="carrito" tabIndex={-1} className="cart-panel" aria-labelledby="cart-title">
      <div className="panel-heading"><h2 id="cart-title">Venta actual</h2><Badge>0 productos</Badge></div>
      <EmptyState title="Tu carrito está vacío" description="Los productos seleccionados aparecerán aquí." icon={<ShoppingBasket size={28} />} />
      <CheckoutSummary />
    </Panel>
  )
}
