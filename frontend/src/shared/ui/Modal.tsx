import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { X } from 'lucide-react'
import { Button, IconButton } from './Button'
import { Dialog } from './Dialog'

export interface ModalProps {
  open: boolean
  title: string
  description?: string
  children: ReactNode
  footer?: ReactNode
  onClose: () => void
  dirty?: boolean
  busy?: boolean
  recovery?: boolean
  wide?: boolean
}

// Children remain mounted: closing a surface must not discard an operation's hook,
// identity or reconciliation state. The feature owns those states and resets them.
export function Modal({ open, title, description, children, footer, onClose, dirty = false, busy = false, recovery = false, wide = false, side = false }: ModalProps & { side?: boolean }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const trigger = useRef<HTMLElement | null>(null)
  const decision = useRef<HTMLDivElement>(null)
  const [discard, setDiscard] = useState(false)
  const descriptionId = useId(); const titleId = useId()
  const blocked = busy || recovery
  useEffect(() => {
    const element = dialog.current
    if (!element) return
    if (open && !element.open) {
      trigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
      element.showModal()
      heading.current?.focus()
    } else if (!open && element.open) {
      element.close()
      if (trigger.current?.isConnected) trigger.current.focus()
    }
    return () => {
      if (element.open) {
        element.close()
        if (trigger.current?.isConnected) trigger.current.focus()
      }
    }
  }, [open])
  useEffect(() => { if (discard) decision.current?.querySelector('button')?.focus() }, [discard])
  function close() {
    if (blocked) return
    setDiscard(false)
    onClose()
  }
  function keepEditing() { setDiscard(false); heading.current?.focus({ preventScroll: true }) }
  function requestClose() {
    if (blocked) return
    if (discard) keepEditing()
    else if (dirty) setDiscard(true)
    else close()
  }
  return <Dialog dialogRef={dialog} label={title} labelledBy={titleId} describedBy={description ? descriptionId : undefined} className={`task-dialog${side ? ' task-drawer' : ''}${wide ? ' task-dialog-wide' : ''}`}
    closeDisabled={blocked} onRequestClose={requestClose}>
    <header className="task-dialog-heading">
      <div><h2 id={titleId} ref={heading} tabIndex={-1}>{title}</h2>
        {description && <p id={descriptionId}>{description}</p>}</div>
      <IconButton label={`Cerrar ${title}`} disabled={blocked} onClick={requestClose}><X size={20} /></IconButton>
    </header>
    <div className="task-dialog-body">
      {discard && <div ref={decision} className="discard-confirmation" role="alert">
        <strong>¿Salir del formulario?</strong><p>Se descartarán los campos sin guardar. Los registros ya confirmados se conservan.</p>
        <div className="operation-actions"><Button variant="secondary" disabled={blocked} onClick={keepEditing}>Seguir editando</Button>
          <Button variant="danger" disabled={blocked} onClick={close}>Descartar cambios</Button></div>
      </div>}
      <div className="task-dialog-content" inert={discard || undefined}>{children}</div>
    </div>
    {(footer || blocked) && <footer className="task-dialog-footer">
      {blocked && <p className="task-dialog-lock" role="status">{recovery ? 'El resultado está pendiente. Consulta la operación antes de salir o repetirla.' : 'Espera mientras se confirma la operación.'}</p>}
      {footer}
    </footer>}
  </Dialog>
}

export function Drawer(props: ModalProps) {
  return <Modal {...props} side />
}
