import { useEffect, useRef } from 'react'

export function Steps({ labels, current, label = 'Progreso de selección', focusOnChange = false }: { labels: readonly string[]; current: number; label?: string; focusOnChange?: boolean }) {
  const activeStep = useRef<HTMLLIElement>(null)
  const previous = useRef(current)
  useEffect(() => {
    const changed = previous.current !== current
    previous.current = current
    const element = activeStep.current
    if (!focusOnChange || !changed || !element?.getClientRects().length) return
    // A background wizard must never take focus away from an open task dialog.
    const modal = document.querySelector('dialog[open]')
    if (!modal || modal.contains(element)) element.focus()
  }, [current, focusOnChange])
  return (
    <ol className="steps" aria-label={label}>
      {labels.map((label, index) => (
        <li key={label} ref={index === current ? activeStep : undefined} tabIndex={focusOnChange && index === current ? -1 : undefined} aria-current={index === current ? 'step' : undefined} className={index === current ? 'active' : index < current ? 'complete' : ''}>
          <span aria-hidden="true">{index + 1}</span>{label}
        </li>
      ))}
    </ol>
  )
}
