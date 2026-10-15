import { Notice } from '../../shared/ui/Notice'
import { PosHeader } from './PosHeader'
import { ProductSearchPanel } from './ProductSearchPanel'
import { SaleCartPanel } from './SaleCartPanel'
import './pos.css'

export function PosPage() {
  return (
    <>
      <PosHeader />
      <Notice>Vista inicial: la búsqueda, el carrito y el cobro se conectarán en F2. Aquí no se registran ventas ni se modifica stock.</Notice>
      <a href="#carrito" className="text-link mobile-cart-link">Ir al carrito vacío</a>
      <div className="pos-layout"><ProductSearchPanel /><SaleCartPanel /></div>
    </>
  )
}
