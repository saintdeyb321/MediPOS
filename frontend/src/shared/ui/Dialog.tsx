import type { KeyboardEvent, ReactNode, RefObject } from 'react'

// Native modal supplies inert background, Escape, and focus restoration.
// Cycle Tab within its controls instead of sending it to browser chrome at either end.
function containTab(event: KeyboardEvent<HTMLDialogElement>) {
  if (event.key !== 'Tab') return
  const controls = [...event.currentTarget.querySelectorAll<HTMLElement>('a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), summary, [tabindex]')]
    .filter(element => element.tabIndex >= 0 && !element.closest('[inert]') && element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden')
  const first = controls[0]
  const last = controls.at(-1)
  const activeIsControl = controls.some(element => element === document.activeElement)
  if (!first || !last) {
    event.preventDefault()
    event.currentTarget.focus()
  } else if (event.shiftKey && (document.activeElement === first || !activeIsControl)) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && (document.activeElement === last || !activeIsControl)) {
    event.preventDefault()
    first.focus()
  }
}

export function Dialog({ dialogRef, label, labelledBy, describedBy, className, children, closeDisabled = false, onRequestClose, onClose }: {
  dialogRef: RefObject<HTMLDialogElement | null>; label: string; className?: string; children: ReactNode
  labelledBy?: string; describedBy?: string
  closeDisabled?: boolean; onRequestClose?: () => void; onClose?: () => void
}) {
  function requestClose() {
    if (closeDisabled) return
    if (onRequestClose) onRequestClose()
    else dialogRef.current?.close()
  }
  return (
    <dialog ref={dialogRef} aria-label={labelledBy ? undefined : label} aria-labelledby={labelledBy} aria-describedby={describedBy} className={className} tabIndex={-1} onKeyDown={containTab}
      onCancel={event => { event.preventDefault(); requestClose() }} onClose={onClose}
      onClick={event => {
        if (event.target !== event.currentTarget) return
        const bounds = event.currentTarget.getBoundingClientRect()
        if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) requestClose()
      }}>
      {children}
    </dialog>
  )
}
