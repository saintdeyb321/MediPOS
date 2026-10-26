import type { ReactNode } from 'react'
import { LayoutGrid } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export function PageHeader({ eyebrow = 'MediPOS · espacio de trabajo', title, description, action, icon: Icon = LayoutGrid }: { eyebrow?: string; title: string; description?: string; action?: ReactNode; icon?: LucideIcon }) {
  return (
    <div className="page-heading">
      <span className="module-heading-icon"><Icon size={26} aria-hidden="true" /></span>
      <div className="page-heading-copy">
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {action}
    </div>
  )
}
