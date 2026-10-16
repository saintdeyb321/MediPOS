import { ShoppingBasket } from 'lucide-react'
import { Panel } from '../../shared/ui/Panel'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/States'
import { CheckoutSummary } from './CheckoutSummary'
import { Button } from '../../shared/ui/Button'
import type { ProductSelection } from './productSelection'

export function SaleCartPanel({ selection, clearSelection }: { selection: ProductSelection | null; clearSelection: () => void }) {
  return (
    <Panel id="carrito" tabIndex={-1} className="cart-panel" aria-labelledby="cart-title">
      <div className="panel-heading"><h2 id="cart-title">Venta actual</h2><Badge>0 productos</Badge></div>
      {selection ? <section className="prepared-selection" aria-label="Selección preparada" aria-live="polite">
        <h3>Selección preparada</h3><p>{selection.product.name}</p><p>Presentación: {selection.unit.name}</p>
        <p>Aún no se agregó a una venta. El carrito se conectará en el siguiente incremento.</p>
        <Button variant="quiet" onClick={clearSelection}>Quitar selección</Button>
      </section> : <EmptyState title="Tu carrito está vacío" description="Selecciona un producto y una presentación para preparar el siguiente paso." icon={<ShoppingBasket size={28} />} />}
      <CheckoutSummary />
    </Panel>
  )
}
