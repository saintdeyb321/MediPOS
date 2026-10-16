import { useState } from 'react'
import { useLocation } from 'react-router'
import { ApiErrorState } from '../access'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { decimalLabel } from '../../shared/api/decimal'
import type { DraftController } from './useSaleDraft'
import { useSaleCheckout } from './useSaleCheckout'
import { emptyPayments, paymentInputs, paymentMethods } from './payments'
import { InternalTicketPanel } from './InternalTicketPanel'

export function SaleCheckoutPanel({ draft }: { draft: DraftController }) {
  return <Checkout key={draft.server?.saleId ?? 'no-sale'} draft={draft} />
}
function Checkout({ draft }: { draft: DraftController }) {
  const checkout = useSaleCheckout(draft)
  const [opened, setOpened] = useState(false)
  const location = useLocation()
  const terminal = draft.server && draft.server.status !== 'draft'
  return <Panel className="checkout-panel" aria-labelledby="checkout-title">
    <div className="panel-heading"><h2 id="checkout-title">Cobro y ticket interno</h2></div>
    <div className="pos-section-body">
      {checkout.error != null && <ApiErrorState error={checkout.error} title="Revisa el resultado antes de cobrar de nuevo" />}
      {checkout.notice && <p role="status">{checkout.notice}</p>}
      {draft.checkoutPending && <Notice tone="warning">Confirmación pendiente de reconciliar. No envíes otro cobro ni prepares otra venta para reemplazarla. Consulta este mismo registro.</Notice>}
      {!terminal && <>
        <p id="checkout-requirements">Primero guarda todas las líneas del borrador con tu caja abierta. El cobro utiliza el total del servidor, no el estimado local.</p>
        <Button aria-describedby="checkout-requirements" disabled={!draft.canCheckout || checkout.busy || checkout.denied} onClick={() => setOpened(true)}>Continuar al cobro</Button>
        {opened && draft.server && <PaymentForm key={draft.server.version} draft={draft} checkout={checkout} />}
      </>}
      {(draft.checkoutPending || checkout.error != null || terminal) && <Button variant="secondary" pending={checkout.busy} disabled={draft.phase === 'loading'} onClick={() => void checkout.consult()}>Consultar resultado de venta</Button>}
      {terminal && <>
        <p role="status">{draft.server?.status === 'confirmed' ? 'Venta confirmada. Los pagos y el stock se registraron en el servidor.' : 'Venta anulada. No está disponible para otro cobro.'}</p>
        {checkout.ticket.isPending && <p role="status">Consultando ticket interno…</p>}
        {checkout.ticket.isError && <ApiErrorState error={checkout.ticket.error} title="El ticket no pudo recuperarse; esto no autoriza repetir el cobro" />}
        <Button variant="quiet" disabled={checkout.ticket.isFetching} onClick={() => void checkout.ticket.refetch()}>Consultar ticket</Button>
        {checkout.ticket.data && !checkout.ticket.isError && <InternalTicketPanel ticket={checkout.ticket.data} />}
        {!draft.checkoutPending && <a className="text-link no-print" href={location.pathname}>Preparar otra venta</a>}
      </>}
    </div>
  </Panel>
}
function PaymentForm({ draft, checkout }: { draft: DraftController; checkout: ReturnType<typeof useSaleCheckout> }) {
  const [fields, setFields] = useState(emptyPayments)
  const [reviewed, setReviewed] = useState(false)
  const sale = draft.server!
  const validation = paymentInputs(fields, sale.totalAmount)
  const disabled = !draft.canCheckout || checkout.busy || checkout.denied
  return <form aria-label="Pagos de la venta" onSubmit={event => {
    event.preventDefault()
    if (!disabled && reviewed && !validation.error) void checkout.confirm(validation.payments)
  }}>
    <p><strong>Total guardado: S/ {decimalLabel(sale.totalAmount, 2)}</strong> · Versión {sale.version}</p>
    <p>Deja vacíos los medios que no uses. Efectivo es el importe aplicado a la venta, sin incluir vuelto. Yape y Plin no requieren número de operación.</p>
    {paymentMethods.map(({ method, label }) => <TextField key={method} label={`${label} (S/)`} inputMode="decimal" maxLength={32}
      disabled={disabled} value={fields[method]} onChange={event => { setFields({ ...fields, [method]: event.target.value }); setReviewed(false) }} />)}
    <p aria-live="polite">Suma de pagos: S/ {decimalLabel(validation.sum, 2)}. {validation.error}</p>
    <label><input type="checkbox" checked={reviewed} disabled={disabled} onChange={event => setReviewed(event.target.checked)} /> Revisé los importes y los medios recibidos. Confirmar registrará pagos y descontará stock.</label>
    <Button type="submit" pending={checkout.busy} disabled={disabled || !reviewed || Boolean(validation.error)}>Confirmar venta y registrar pagos</Button>
  </form>
}
