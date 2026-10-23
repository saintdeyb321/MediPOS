import type { ReactNode } from 'react'

export function PageToolbar({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  return <div className="page-toolbar"><div className="page-toolbar-fields">{children}</div>{actions && <div className="operation-actions">{actions}</div>}</div>
}

export function FilterBar({ children, label = 'Filtros de consulta' }: { children: ReactNode; label?: string }) {
  return <div className="filter-bar" role="group" aria-label={label}>{children}</div>
}
