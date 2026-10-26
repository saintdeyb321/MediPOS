import { ArrowLeft, ChevronRight, Home } from 'lucide-react'
import { Link } from 'react-router'

export function Breadcrumbs({ items, back }: { items: readonly { label: string; to?: string }[]; back?: { label: string; to: string } }) {
  return <div className="module-navigation">
    <nav aria-label="Ruta de navegación"><ol className="breadcrumbs">{items.map((item, index) => <li key={index}>
      {index === 0 && <Home size={15} aria-hidden="true" />}
      {item.to ? <Link to={item.to}>{item.label}</Link> : <span aria-current="page">{item.label}</span>}
      {index < items.length - 1 && <ChevronRight size={14} aria-hidden="true" />}
    </li>)}</ol></nav>
    {back && <Link className="text-link" to={back.to}><ArrowLeft size={16} aria-hidden="true" />{back.label}</Link>}
  </div>
}
