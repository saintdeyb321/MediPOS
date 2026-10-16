import type { JsonNumber } from '../../shared/api/exactJson'
import { decimalLabel } from '../../shared/api/decimal'

export function CheckoutSummary({ total }: { total: JsonNumber | null }) {
  return (
    <div className="cart-totals">
      <dl><div className="cart-grand-total"><dt>Total estimado local</dt><dd>{total ? `S/ ${decimalLabel(total, 2)}` : 'Revisa el carrito'}</dd></div></dl>
      <p>Estimación del carrito. Consulta el borrador guardado y el panel de cobro para conocer el estado real de la venta.</p>
    </div>
  )
}
