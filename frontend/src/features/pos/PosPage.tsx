import { useState } from 'react'
import { useWorkspace } from '../access'
import { Notice } from '../../shared/ui/Notice'
import { PosHeader } from './PosHeader'
import { ProductSearchPanel } from './ProductSearchPanel'
import { SaleCartPanel } from './SaleCartPanel'
import type { ProductSelection } from './productSelection'
import './pos.css'

export function PosPage() {
  const { tenantId, branchId } = useWorkspace()
  return <PosWorkspace key={`${tenantId}/${branchId}`} />
}

function PosWorkspace() {
  const [selection, setSelection] = useState<ProductSelection | null>(null)
  return (
    <>
      <PosHeader />
      <Notice>Consulta productos de tu sucursal y prepara una presentación. El carrito y el cobro aún no están habilitados; aquí no se registran ventas ni se modifica stock.</Notice>
      <a href="#carrito" className="text-link mobile-cart-link">Ir al carrito vacío</a>
      <div className="pos-layout"><ProductSearchPanel onSelect={setSelection} /><SaleCartPanel selection={selection} clearSelection={() => setSelection(null)} /></div>
    </>
  )
}
