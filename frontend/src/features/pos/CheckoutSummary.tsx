import { CreditCard } from 'lucide-react'
import { Button } from '../../shared/ui/Button'

export function CheckoutSummary() {
  return (
    <div className="cart-totals">
      <dl><div><dt>Subtotal</dt><dd>S/ 0.00</dd></div><div className="cart-grand-total"><dt>Total</dt><dd>S/ 0.00</dd></div></dl>
      <Button disabled aria-describedby="checkout-note"><CreditCard size={20} aria-hidden="true" />Continuar al cobro</Button>
      <p id="checkout-note">Cobro disponible en F2. No hay una venta creada ni un importe por cobrar.</p>
    </div>
  )
}
