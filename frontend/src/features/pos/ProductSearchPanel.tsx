import { Search } from 'lucide-react'
import { Panel } from '../../shared/ui/Panel'
import { SearchField } from '../../shared/ui/TextField'
import { EmptyState } from '../../shared/ui/States'

export function ProductSearchPanel() {
  return (
    <Panel className="product-workspace" aria-label="Búsqueda y resultados">
      <div className="pos-search"><SearchField label="Buscar un producto" placeholder="Nombre, ingrediente o código" disabled hint="Disponible en F2. Se buscará en la sucursal seleccionada." /></div>
      <div className="product-results">
        <h2>Resultados</h2>
        <EmptyState title="El producto correcto, a la mano." description="Los resultados mostrarán presentación, stock y precio de esta sucursal. No hay productos de ejemplo." icon={<Search size={28} />} />
      </div>
    </Panel>
  )
}
