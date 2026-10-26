import type { ReactNode } from 'react'
import { AlertCircle, Inbox, LoaderCircle } from 'lucide-react'
import { Button } from './Button'

export function LoadingState({ label = 'Preparando tu espacio…' }: { label?: string }) {
  return (
    <div className="state-block" role="status">
      <LoaderCircle className="spin" size={28} aria-hidden="true" />
      <h2>{label}</h2>
      <p>Estamos recuperando la información disponible.</p>
    </div>
  )
}

export function EmptyState({ title, description, icon, action }: { title: string; description: string; icon?: ReactNode; action?: ReactNode }) {
  return (
    <div className="state-block">
      <span className="state-icon" aria-hidden="true">{icon ?? <Inbox size={28} />}</span>
      <h2>{title}</h2>
      <p>{description}</p>
      {action}
    </div>
  )
}

export function ErrorState({ message, reference, retry, title = 'No pudimos cargar la información' }: { message: string; reference?: string; retry?: () => void; title?: string }) {
  return (
    <div className="state-block" role="alert">
      <span className="state-icon error"><AlertCircle aria-hidden="true" /></span>
      <h2>{title}</h2>
      <p>{message}</p>
      {reference && <small>Referencia de soporte: {reference}</small>}
      {retry && <Button variant="secondary" onClick={retry}>Volver a intentar</Button>}
    </div>
  )
}
