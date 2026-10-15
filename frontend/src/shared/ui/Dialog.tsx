import type { KeyboardEvent, ReactNode, RefObject } from 'react'

// Native modal supplies inert background, Escape, and focus restoration.
// Cycle Tab within its controls instead of sending it to browser chrome at either end.
function containTab(event: KeyboardEvent<HTMLDialogElement>) {
  if (event.key !== 'Tab') return
  const controls = [...event.currentTarget.querySelectorAll<HTMLElement>('a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary, [tabindex]')]
    .filter(element => element.tabIndex >= 0 && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden')
  const first = controls[0]
  const last = controls.at(-1)
  if (!first || !last) {
    event.preventDefault()
    event.currentTarget.focus()
  } else if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}

export function Dialog({ dialogRef, label, className, children }: { dialogRef: RefObject<HTMLDialogElement | null>; label: string; className?: string; children: ReactNode }) {
  return (
    <dialog ref={dialogRef} aria-label={label} className={className} tabIndex={-1} onKeyDown={containTab}
      onClick={event => { if (event.target === event.currentTarget) event.currentTarget.close() }}>
      {children}
    </dialog>
  )
}
