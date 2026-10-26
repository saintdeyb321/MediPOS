import { useEffect, useRef, useState } from 'react'
import { ApiErrorState, useWorkspace } from '../access'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { Panel } from '../../shared/ui/Panel'
import { TextField } from '../../shared/ui/TextField'
import { Modal } from '../../shared/ui/Modal'
import { FormFooter } from '../../shared/ui/FormLayout'
import { decimalLabel } from '../../shared/api/decimal'
import type { DraftController } from './useSaleDraft'
import { useSaleCheckout } from './useSaleCheckout'
import { emptyPayments, paymentInputs, paymentMethods } from './payments'
import { InternalTicketPanel } from './InternalTicketPanel'

export function SaleCheckoutPanel({ draft, canStartPayment = true }: { draft: DraftController; canStartPayment?: boolean }) {
  const checkout = useSaleCheckout(draft)
  const [opened, setOpened] = useState(false)
  const [paymentFormActive, setPaymentFormActive] = useState(false)
  const [dirty, setDirty] = useState(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const terminal = draft.server && draft.server.status !== 'draft'
  return <Panel className="checkout-panel" aria-labelledby="checkout-title">
    <h2 id="checkout-title" className="sr-only">Cobro</h2>
    <div className="pos-section-body">
      {checkout.error != null && <ApiErrorState error={checkout.error} title="Revisa el resultado antes de cobrar de nuevo" />}
      {checkout.notice && <p role="status">{checkout.notice}</p>}
      {draft.checkoutPending && <Notice tone="warning">Operación pendiente de reconciliar. No envíes otro cobro ni prepares otra venta para reemplazarla. Consulta este mismo registro.</Notice>}
      {!terminal && <>
        {canStartPayment && <Button pending={draft.preparing} disabled={!draft.canPrepare || checkout.busy || checkout.denied}
          onClick={async () => { const ready = await draft.prepare(); if (mounted.current) { setDirty(false); setPaymentFormActive(ready); setOpened(ready) } }}>Cobrar</Button>}
        {draft.server && !draft.cashMatches && <p>Consulta la caja original de esta venta antes de cobrar.</p>}
        {!draft.seller && <Notice tone="warning">Esta venta pertenece a otra membresía. No puedes editarla ni cobrarla.</Notice>}
        {draft.notice && draft.phase === 'idle' && <p role="status">{draft.notice}</p>}
      </>}
      {(draft.checkoutPending || checkout.error != null || terminal) && <Button variant="secondary" pending={checkout.busy} disabled={draft.phase === 'loading'} onClick={() => void checkout.consult()}>Consultar resultado de venta</Button>}
      {draft.checkoutPending && paymentFormActive && !opened && !terminal && <Button data-recovery-focus variant="secondary" disabled={checkout.busy} onClick={() => setOpened(true)}>Revisar cobro pendiente sin reenviar</Button>}
      {terminal && <>
        <p role="status">{draft.server?.status === 'confirmed' ? 'VENTA REALIZADA. Pagos y stock verificados en el servidor.' : 'Venta anulada. No está disponible para otro cobro.'}</p>
        {checkout.ticket.isPending && <p role="status">Consultando ticket interno…</p>}
        {checkout.ticket.isError && <ApiErrorState error={checkout.ticket.error} title="El ticket no pudo recuperarse; esto no autoriza repetir el cobro" />}
        <Button variant="quiet" disabled={checkout.ticket.isFetching} onClick={() => void checkout.ticket.refetch()}>Consultar ticket</Button>
        {checkout.ticket.data && !checkout.ticket.isError && checkout.ticket.data.status === draft.server?.status && <InternalTicketPanel ticket={checkout.ticket.data} />}
        {!draft.checkoutPending && <Button variant="secondary" className="no-print" disabled={checkout.busy} onClick={() => {
          if (draft.nextSale()) { setOpened(false); setPaymentFormActive(false); checkout.clearResult() }
        }}>Seguir vendiendo sin imprimir</Button>}
        {draft.server?.status === 'confirmed' && <VoidSaleForm draft={draft} checkout={checkout} />}
      </>}
    </div>
    <Modal open={opened && !terminal} title="Revisar y cobrar venta" description="Confirma únicamente los importes que realmente recibiste." onClose={() => { setOpened(false); setPaymentFormActive(false); setDirty(false) }} onRecoveryClose={() => setOpened(false)} dirty={dirty} busy={checkout.busy} recovery={draft.checkoutPending}>
      <div className="pos-section-body" onChangeCapture={() => setDirty(true)}>
        {checkout.error != null && <ApiErrorState error={checkout.error} title="Revisa el resultado antes de repetir" />}
        {checkout.notice && <p role="status">{checkout.notice}</p>}
        {draft.checkoutPending && <Notice tone="warning">El cobro sigue pendiente de verificar. Conservamos esta venta; no se enviará un segundo cobro.</Notice>}
        {(draft.checkoutPending || checkout.error != null) && <Button variant="secondary" pending={checkout.busy} onClick={() => void checkout.consult()}>Consultar esta misma venta</Button>}
        {paymentFormActive && draft.server && !terminal && <PaymentForm key={`${draft.server.saleId}/${draft.server.version}`} draft={draft} checkout={checkout} />}
      </div>
    </Modal>
  </Panel>
}
function VoidSaleForm({ draft, checkout }: { draft: DraftController; checkout: ReturnType<typeof useSaleCheckout> }) {
  const { access } = useWorkspace()
  const [reason, setReason] = useState(''); const [reviewed, setReviewed] = useState(false)
  const [opened, setOpened] = useState(false)
  if (access.role !== 'owner' && !draft.seller) return null
  return <div className="no-print"><Button variant="quiet" disabled={checkout.busy || draft.checkoutPending || checkout.denied} onClick={() => setOpened(true)}>Anular esta venta</Button>
    {draft.checkoutPending && !opened && <Button data-recovery-focus variant="secondary" disabled={checkout.busy} onClick={() => setOpened(true)}>Revisar anulación pendiente sin reenviar</Button>}
    <Modal open={opened} title="Anular venta confirmada" dirty={Boolean(reason || reviewed)} busy={checkout.busy} recovery={draft.checkoutPending} onClose={() => { setOpened(false); setReason(''); setReviewed(false) }} onRecoveryClose={() => setOpened(false)}>
    {checkout.error != null && <ApiErrorState error={checkout.error} />}
    {draft.checkoutPending && <><Notice tone="warning">Anulación pendiente. Consulta la misma venta antes de otra operación.</Notice><Button disabled={checkout.busy} onClick={() => void checkout.consult()}>Consultar resultado de la venta</Button></>}
    <form className="operation-body" aria-label="Anulación de venta" onSubmit={event => {
    event.preventDefault(); if (reviewed) void checkout.voidSale(reason)
  }}>
    <TextField label="Motivo de anulación" value={reason} maxLength={512} disabled={checkout.busy || draft.checkoutPending} onChange={event => { setReason(event.target.value); setReviewed(false) }} />
    <p>Se registrarán reversiones de pagos y stock; no se borra la venta. La caja original debe seguir abierta y disponer del efectivo necesario para la devolución.</p>
    <label><input type="checkbox" checked={reviewed} disabled={checkout.busy || draft.checkoutPending} onChange={event => setReviewed(event.target.checked)} /> Revisé la devolución y autorizo la anulación.</label>
    <FormFooter><Button type="submit" variant="danger" disabled={!reviewed || !reason.trim() || checkout.busy || draft.checkoutPending || checkout.denied}>Confirmar anulación</Button></FormFooter>
  </form></Modal></div>
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
    <p><strong>Total a cobrar: S/ {decimalLabel(sale.totalAmount, 2)}</strong></p>
    <p>Deja vacíos los medios que no uses. Efectivo es el importe aplicado a la venta, sin incluir vuelto. Yape y Plin no requieren número de operación.</p>
    {paymentMethods.map(({ method, label }) => <TextField key={method} label={`${label} (S/)`} inputMode="decimal" maxLength={32}
      disabled={disabled} value={fields[method]} onChange={event => { setFields({ ...fields, [method]: event.target.value }); setReviewed(false) }} />)}
    <p aria-live="polite">Suma de pagos: S/ {decimalLabel(validation.sum, 2)}. {validation.error}</p>
    <label><input type="checkbox" checked={reviewed} disabled={disabled} onChange={event => setReviewed(event.target.checked)} /> Revisé los importes y los medios recibidos. Confirmar registrará pagos y descontará stock.</label>
    <FormFooter><Button type="submit" pending={checkout.busy} disabled={disabled || !reviewed || Boolean(validation.error)}>Confirmar venta y registrar pagos</Button></FormFooter>
  </form>
}
