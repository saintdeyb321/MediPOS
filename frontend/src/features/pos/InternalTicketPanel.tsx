import { Button } from '../../shared/ui/Button'
import { decimalLabel } from '../../shared/api/decimal'
import type { InternalTicket } from './tickets.api'
import { paymentMethods } from './payments'

export function InternalTicketPanel({ ticket }: { ticket: InternalTicket }) {
  return <article className="internal-ticket" aria-label="Ticket interno">
    <h3>Ticket interno · {ticket.statusLabel}</h3>
    <p>{ticket.currentDisplayData.businessName} · {ticket.currentDisplayData.branchName}</p>
    <p>{ticket.ticketNumber}</p>
    <p>{new Intl.DateTimeFormat('es-PE', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Lima' }).format(new Date(ticket.saleDateTime))} (Lima)</p>
    <p>Atendió: {ticket.currentDisplayData.sellerDisplayName}</p>
    {ticket.isVoided && <p>Anulada: {ticket.voidReason}</p>}
    <ul>{ticket.lines.map((line, index) => <li key={index}>
      <strong>{line.productName}</strong> · {line.unitName} × {decimalLabel(line.quantity)}
      <p>Precio por unidad base: S/ {decimalLabel(line.unitPrice, 2)} · Unidades base: {decimalLabel(line.baseQuantity)}</p>
      <p>Subtotal: S/ {decimalLabel(line.lineTotal, 2)}</p>
    </li>)}</ul>
    <p><strong>Total registrado: S/ {decimalLabel(ticket.totalAmount, 2)}</strong></p>
    <ul>{ticket.payments.map((payment, index) => <li key={index}>{paymentMethods.find(item => item.method === payment.method)?.label ?? payment.method}: S/ {decimalLabel(payment.amount, 2)}</li>)}</ul>
    <p>{ticket.footer}</p>
    <p>No es boleta ni factura SUNAT. Los datos de presentación del negocio son los actuales; las líneas e importes corresponden a la venta.</p>
    <Button className="no-print" variant="secondary" onClick={() => window.print()}>Imprimir ticket interno (opcional)</Button>
  </article>
}
