import { useId, useRef } from 'react'
import type { ReactNode } from 'react'
import { CircleHelp } from 'lucide-react'

export function HelpPopover({ label, children }: { label: string; children: ReactNode }) {
  const id = useId()
  const trigger = useRef<HTMLButtonElement>(null)
  const popover = useRef<HTMLDivElement>(null)
  return <div className="context-help">
    <button ref={trigger} type="button" className="context-help-trigger" aria-label={label} popoverTarget={id} onClick={() => {
      const bounds = trigger.current?.getBoundingClientRect()
      const panel = popover.current
      if (!bounds || !panel) return
      panel.style.setProperty('--help-x', `${bounds.left}px`)
      panel.style.setProperty('--help-y', `${bounds.bottom + 4}px`)
      panel.style.setProperty('--help-bottom', `${innerHeight - bounds.top + 4}px`)
      panel.dataset.side = innerHeight - bounds.bottom >= 160 ? 'below' : 'above'
    }}><CircleHelp size={17} aria-hidden="true" /></button>
    <div id={id} ref={popover} popover="auto" className="context-help-popover" role="group" aria-label={label}>{children}</div>
  </div>
}
