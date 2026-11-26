import type { ReactNode } from 'react'
import { Panel } from './Panel'

export function DataSection({ tabs, toolbar, children, footer, className = '' }: {
  tabs?: ReactNode; toolbar?: ReactNode; children: ReactNode; footer?: ReactNode; className?: string
}) {
  return <Panel className={`data-section ${className}`}>
    {tabs && <div className="data-section-tabs">{tabs}</div>}
    {toolbar && <div className="data-section-toolbar">{toolbar}</div>}
    <div className="data-section-content">{children}</div>
    {footer && <div className="data-section-footer">{footer}</div>}
  </Panel>
}
