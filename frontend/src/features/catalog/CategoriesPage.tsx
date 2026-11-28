import { Link } from 'react-router'
import { LayoutGrid } from 'lucide-react'
import { ApiErrorState, useRuntime, useWorkspace } from '../access'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { useCategories } from './useCategories'

export function CategoriesPage() {
  const w = useWorkspace()
  if (w.access.role !== 'owner') return <Panel role="alert">Categorías requiere acceso de propietario.</Panel>
  return <CategoryDirectory />
}

function CategoryDirectory() {
  const w = useWorkspace()
  const { http } = useRuntime()
  const categories = useCategories()
  return <section><PageHeader icon={LayoutGrid} title="Categorías" />
    {!http ? <p>Se requiere una conexión HTTPS con MediPOS.</p>
      : categories.isError ? <ApiErrorState error={categories.error} retry={() => void categories.refetch()} />
      : categories.isPending ? <p role="status">Consultando categorías…</p>
      : categories.data.length === 0 ? <p>No hay categorías disponibles.</p>
      : <ul className="catalog-card-grid">{categories.data.map(category => <li key={category.id}><Panel>
        <h2>{category.name}</h2><Link className="button secondary" to={w.base + '/productos?categoria=' + category.id}>Ver productos de {category.name}</Link>
      </Panel></li>)}</ul>}
  </section>
}
