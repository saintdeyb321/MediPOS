import type { JsonNumber } from '../../shared/api/exactJson'
import { decimalLabel } from '../../shared/api/decimal'

export function CheckoutSummary({ total }: { total: JsonNumber | null }) {
  return (
    <div className="cart-totals">
      <dl><div className="cart-grand-total"><dt>Total estimado</dt><dd>{total ? `S/ ${decimalLabel(total, 2)}` : 'Revisa el carrito'}</dd></div></dl>
    </div>
  )
}
