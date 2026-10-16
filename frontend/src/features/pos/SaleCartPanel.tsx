import { ShoppingBasket, Trash2 } from 'lucide-react'
import { Panel } from '../../shared/ui/Panel'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/States'
import { CheckoutSummary } from './CheckoutSummary'
import { Button } from '../../shared/ui/Button'
import { TextField } from '../../shared/ui/TextField'
import { Notice } from '../../shared/ui/Notice'
import { decimalLabel } from '../../shared/api/decimal'
import { cartError, estimateTotal, lineAmounts, lineKey } from './cart'
import type { CartLine } from './cart'

function CartItem({ line, disabled, change, remove }: { line: CartLine; disabled: boolean; change: (line: CartLine) => void; remove: () => void }) {
  const amounts = lineAmounts(line)
  const unit = line.units.find(item => item.id === line.unitId)
  return <li className="cart-item">
    <h3>{line.name}</h3>
    <div className="form-field"><label>Presentación en carrito de {line.name}
      <select value={line.unitId} disabled={disabled} onChange={event => change({ ...line, unitId: event.target.value })}>
        {line.units.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select></label></div>
    <TextField label={`Cantidad en carrito de ${line.name}, ${unit?.name ?? 'presentación'}`} inputMode="decimal" value={line.quantity}
      maxLength={32} disabled={disabled} onChange={event => change({ ...line, quantity: event.target.value })}
      error={!amounts ? 'Revisa la cantidad y su conversión exacta.' : undefined} />
    <dl className="cart-line-facts">
      <div><dt>Unidades base</dt><dd>{amounts ? decimalLabel(amounts.base) : '—'}</dd></div>
      <div><dt>Precio estimado por presentación</dt><dd>{amounts ? `S/ ${decimalLabel(amounts.unitPrice, 2)}` : '—'}</dd></div>
      <div><dt>Subtotal estimado</dt><dd>{amounts ? `S/ ${decimalLabel(amounts.total, 2)}` : '—'}</dd></div>
    </dl>
    <p className="muted">Stock consultado: {line.stock ? `${decimalLabel(line.stock)} unidades base` : 'sin consulta actual; actualízalo mediante el buscador'}. El borrador no reserva stock.</p>
    <Button variant="quiet" disabled={disabled} onClick={remove} aria-label={`Quitar ${line.name}, ${unit?.name ?? 'presentación'}`}><Trash2 size={16} aria-hidden="true" />Quitar</Button>
  </li>
}
export function SaleCartPanel({ lines, change, disabled, message }: {
  lines: CartLine[]; change: (lines: CartLine[]) => void; disabled: boolean; message: string
}) {
  const error = cartError(lines)
  return (
    <Panel id="carrito" tabIndex={-1} className="cart-panel" aria-labelledby="cart-title">
      <div className="panel-heading"><h2 id="cart-title">Carrito local</h2><Badge>{lines.length} líneas</Badge></div>
      {message && <p className="pos-section-body" role="status">{message}</p>}
      {error && <Notice tone="warning">{error}</Notice>}
      {lines.length ? <ul className="cart-list">{lines.map((line, index) => <CartItem key={lineKey(line)} line={line} disabled={disabled}
        change={updated => change(lines.map((current, at) => at === index ? updated : current))}
        remove={() => change(lines.filter((_, at) => at !== index))} />)}</ul> :
        <EmptyState title="Tu carrito está vacío" description="Busca un producto, elige su presentación y cantidad y pulsa Seleccionar." icon={<ShoppingBasket size={28} />} />}
      <CheckoutSummary total={error ? null : estimateTotal(lines)} />
    </Panel>
  )
}
