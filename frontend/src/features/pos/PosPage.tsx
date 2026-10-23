import { useState } from 'react'
import { useWorkspace } from '../access'
import { CashSessionPanel, useCashSession } from '../cash'
import { Notice } from '../../shared/ui/Notice'
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
  const draft = useSaleDraft(lines, setLines, cash.query.isFetching || cash.pending || cash.uncertain ? null : cash.session)
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
    <>
      <PosHeader />
      <Notice>1. Busca y añade productos. 2. Revisa el carrito. 3. Cobra con tu caja abierta.</Notice>
      <CashSessionPanel cash={cash} />
      <a href="#carrito" className="button secondary mobile-cart-link">Ver carrito · {lines.length} líneas</a>
      <div className="pos-layout"><div id="buscador-pos"><ProductSearchPanel onSelect={select} disabled={draft.locked} /></div>
        <div className="pos-sale"><SaleCartPanel lines={lines} change={changeCart} disabled={draft.locked} message={message} /><SaleDraftPanel draft={draft} /><SaleCheckoutPanel draft={draft} /></div></div>
    </>
  )
}
