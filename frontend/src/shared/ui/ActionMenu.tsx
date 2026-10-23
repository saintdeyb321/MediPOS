import { useRef } from 'react'
import type { ReactNode } from 'react'
import { ChevronDown } from 'lucide-react'

export function ActionMenu({ label = 'Acciones', children }: { label?: string; children: ReactNode }) {
  const ref = useRef<HTMLDetailsElement>(null)
  return <details ref={ref} className="action-menu" onKeyDown={event => {
    if (event.key === 'Escape') { event.preventDefault(); if (ref.current) { ref.current.open = false; ref.current.querySelector('summary')?.focus() } }
  }} onBlur={event => { if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) event.currentTarget.open = false }}>
    <summary>{label}<ChevronDown size={16} aria-hidden="true" /></summary><div className="action-menu-content">{children}</div>
  </details>
}
