import { add, compare, decimalInput, maximumMoney, zero } from '../../shared/api/decimal'
import type { JsonNumber } from '../../shared/api/exactJson'
import type { PaymentMethod, SalePaymentInput } from './sales.api'

export const paymentMethods: { method: PaymentMethod; label: string }[] = [
  { method: 'cash', label: 'Efectivo' }, { method: 'yape', label: 'Yape' }, { method: 'plin', label: 'Plin' },
  { method: 'card', label: 'Tarjeta' }, { method: 'transfer', label: 'Transferencia' },
]
export type PaymentFields = Record<PaymentMethod, string>
export const emptyPayments = (): PaymentFields => ({ cash: '', yape: '', plin: '', card: '', transfer: '' })
// Blank means unused; each selected method must be positive, exact and unique.
export function paymentInputs(fields: PaymentFields, total: JsonNumber): { payments: SalePaymentInput[]; error: string | null; sum: JsonNumber } {
  const payments: SalePaymentInput[] = []
  let sum = zero
  for (const { method } of paymentMethods) {
    if (!fields[method].trim()) continue
    const amount = decimalInput(fields[method], maximumMoney, 4)
    if (!amount) return { payments, sum, error: 'Cada importe utilizado debe ser positivo y tener como máximo cuatro decimales.' }
    payments.push({ method, amount }); sum = add(sum, amount)
  }
  return { payments, sum, error: !payments.length ? 'Indica al menos un medio de pago.' : compare(sum, total) !== 0 ? 'Los pagos deben sumar exactamente el total guardado. Registra el importe aplicado, sin incluir vuelto.' : null }
}
