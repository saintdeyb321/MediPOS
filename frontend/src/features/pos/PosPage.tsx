import { ArrowLeft, Barcode, CreditCard, Search, ShoppingBasket } from 'lucide-react'
import { Link } from 'react-router'
import { useWorkspace } from '../../hooks/useSession'
import { PageHeading } from '../../components/ui'
export function PosPage() {
  const { base, branch } = useWorkspace()
  return <><PageHeading eyebrow="PUNTO DE VENTA" title="Una atención ágil, de principio a fin." description={branch.name} action={<Link className="button secondary" to={base}><ArrowLeft size={16} /> Volver al inicio</Link>} />
    <div className="notice info"><span className="badge blue">Vista inicial</span><p>La búsqueda, el carrito y el cobro se conectarán en F2. Aquí no se registran ventas ni se modifica stock.</p></div>
    <div className="pos-layout"><section className="panel product-workspace"><div className="pos-search"><label htmlFor="product-search">Buscar un producto</label><div className="search-field"><Search size={20} /><input id="product-search" placeholder="Nombre, ingrediente, código o código de barras" disabled aria-describedby="pos-search-note" /><Barcode size={21} /></div><p id="pos-search-note">La búsqueda estará disponible al conectar el flujo de ventas.</p></div><div className="product-results"><div className="table-scroll"><table><caption className="sr-only">Resultados de búsqueda de productos, aún sin conectar</caption><thead><tr><th>Producto</th><th>Presentación</th><th>Stock</th><th>Precio</th></tr></thead><tbody><tr><td colSpan={4}><div className="state-block"><span className="state-icon"><Search size={25} /></span><h2>El producto correcto, a la mano.</h2><p>Aquí podrás buscar y elegir productos de esta sucursal. Sin resultados simulados.</p></div></td></tr></tbody></table></div></div></section>
      <section className="panel cart-panel"><div className="panel-heading"><h2>Venta actual</h2><span className="badge neutral">0 productos</span></div><div className="cart-empty"><ShoppingBasket size={43} strokeWidth={1.4} /><h3>Tu carrito está vacío</h3><p>Los productos seleccionados aparecerán aquí.</p></div><div className="cart-totals"><span>Subtotal <strong>S/ 0.00</strong></span><span className="cart-grand-total">Total <strong>S/ 0.00</strong></span><button className="button primary" disabled><CreditCard size={18} /> Continuar al cobro</button><p>No hay una venta creada ni un importe por cobrar.</p></div></section>
    </div></>
}
