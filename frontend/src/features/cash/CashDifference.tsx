import { compare, decimalLabel, zero } from '../../shared/api/decimal'
import type { JsonNumber } from '../../shared/api/exactJson'
import { Notice } from '../../shared/ui/Notice'

export function CashDifference({ value }: { value: JsonNumber }) {
  const sign = compare(value, zero)
  return <Notice tone={sign === 0 ? 'info' : 'warning'}><div><strong>{sign === 0 ? 'Cuadre exacto' : `${sign > 0 ? 'Sobran' : 'Faltan'} S/ ${decimalLabel(value, 2).replace(/^-/, '')}`}</strong><p className="data-caption">Diferencia final registrada por el servidor. Los pagos electrónicos se mantienen separados del efectivo.</p></div></Notice>
}
