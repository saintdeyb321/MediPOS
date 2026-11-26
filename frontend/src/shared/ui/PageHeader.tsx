import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { LayoutGrid } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export function PageHeader({ eyebrow = 'MediPOS · espacio de trabajo', title, description, action, icon: Icon = LayoutGrid, focusOnChange = false }: { eyebrow?: string; title: string; description?: string; action?: ReactNode; icon?: LucideIcon; focusOnChange?: boolean }) {
  const heading = useRef<HTMLHeadingElement>(null)
  const previousTitle = useRef(title)
  useEffect(() => {
    if (focusOnChange && previousTitle.current !== title) heading.current?.focus()
    previousTitle.current = title
  }, [focusOnChange, title])
  return (
    <div className="page-heading">
      <span className="module-heading-icon"><Icon size={26} aria-hidden="true" /></span>
      <div className="page-heading-copy">
        {eyebrow && <p className="eyebrow">{eyebrow}</p>}
        <h1 ref={heading} tabIndex={focusOnChange ? -1 : undefined}>{title}</h1>
        {description && <p>{description}</p>}
      </div>
      {action && <div className="page-heading-action">{action}</div>}
    </div>
  )
}
