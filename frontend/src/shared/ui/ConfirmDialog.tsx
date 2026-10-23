import type { ReactNode } from 'react'
import { Button } from './Button'
import { Modal } from './Modal'

export function ConfirmDialog({ open, title, children, confirmLabel, onConfirm, onClose, pending = false, recovery = false, disabled = false }: {
  open: boolean; title: string; children: ReactNode; confirmLabel: string; onConfirm: () => void; onClose: () => void; pending?: boolean; recovery?: boolean; disabled?: boolean
}) {
  return <Modal open={open} title={title} onClose={onClose} busy={pending} recovery={recovery} footer={<>
    <Button variant="secondary" disabled={pending || recovery} onClick={onClose}>Cancelar</Button>
    <Button variant="danger" pending={pending} disabled={disabled || recovery} onClick={onConfirm}>{confirmLabel}</Button>
  </>}>{children}</Modal>
}
