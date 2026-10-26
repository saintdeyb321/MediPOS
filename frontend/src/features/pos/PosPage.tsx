import { useState } from 'react'
import { useWorkspace } from '../access'
import { CashAge, CashManagement, useCashSession } from '../cash'
import { useSearchParams } from 'react-router'
import { Badge } from '../../shared/ui/Badge'
import { PosHeader } from './PosHeader'
import { ProductSearchPanel } from './ProductSearchPanel'
import { SaleCartPanel } from './SaleCartPanel'
import type { ProductSelection } from './productSelection'
import { addSelection, lineKey } from './cart'
import type { CartLine } from './cart'
import { useSaleDraft } from './useSaleDraft'
import { SaleDraftPanel } from './SaleDraftPanel'
import { SaleCheckoutPanel } from './SaleCheckoutPanel'
import './pos.css'

export function PosPage() {
  const { tenantId, branchId, session, access } = useWorkspace()
  return <PosWorkspace key={`${session.userId}/${access.membershipId}/${tenantId}/${branchId}`} />
}

function PosWorkspace() {
  const [lines, setLines] = useState<CartLine[]>([])
  const [message, setMessage] = useState('')
  const cash = useCashSession()
  const [parameters] = useSearchParams(); const closingCash = parameters.has('closingCash')
  const draft = useSaleDraft(lines, setLines, cash.query.isFetching || cash.pending || cash.uncertain || closingCash ? null : cash.session)
  function changeCart(next: CartLine[]) {
    if (new Set(next.map(lineKey)).size !== next.length) { setMessage('Esta presentación ya está en el carrito. Ajusta su cantidad sin duplicar la línea.'); return }
    setLines(next); setMessage('Carrito actualizado.')
  }
  function select(selection: ProductSelection) {
    if (draft.locked) return
    try { setLines(addSelection(lines, selection)); setMessage(`${selection.product.name} agregado al carrito.`) }
    catch (error) { setMessage(error instanceof Error ? error.message : 'Revisa el carrito antes de agregar.') }
  }
  return (
    <section className="pos-workbench" aria-label="Venta en sucursal">
      <PosHeader status={<><Badge tone={cash.uncertain || cash.query.isError || closingCash ? 'warning' : cash.session ? 'success' : 'neutral'}>{cash.query.isPending ? 'Consultando caja' : closingCash ? 'Cierre por conciliar' : cash.uncertain ? 'Apertura por conciliar' : cash.query.isError ? 'Caja sin verificar' : cash.session ? 'Caja abierta' : 'Caja cerrada'}</Badge>{cash.session && <CashAge openedAt={cash.session.openedAt} compact />}{draft.checkoutPending && <Badge tone="warning">Venta por conciliar</Badge>}</>} />
      <a href="#carrito" className="button secondary mobile-cart-link">Ver carrito · {lines.length} líneas</a>
      <div className="pos-layout"><div id="buscador-pos"><ProductSearchPanel onSelect={select} disabled={draft.locked} /></div>
        <div className="pos-sale"><SaleCartPanel lines={lines} change={changeCart} disabled={draft.locked} message={message} /><div className="ticket-actions"><CashManagement cash={cash} embedded blocked={draft.checkoutPending || draft.preparing} /><SaleCheckoutPanel draft={draft} canStartPayment={Boolean(cash.session && !cash.uncertain && !closingCash)} /><SaleDraftPanel draft={draft} /></div></div></div>
    </section>
  )
}
