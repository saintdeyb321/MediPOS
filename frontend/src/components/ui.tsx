import { AlertCircle, ArrowLeft, HeartPulse, LoaderCircle } from 'lucide-react'
import { Link } from 'react-router'
import type { ReactNode } from 'react'
import { ApiError } from '../services/http'
import { errorMessage } from '../services/errors'

export function Brand({ compact = false }: { compact?: boolean }) {
  return <span className="brand"><span className="brand-mark" aria-hidden="true"><HeartPulse size={25} strokeWidth={2.3} /></span>
    {!compact && <span>Medi<span className="brand-accent">POS</span><small>EL PULSO DE TU BOTICA</small></span>}</span>
}
export function Loading({ label = 'Preparando tu espacio…' }: { label?: string }) {
  return <div className="state-block" role="status"><LoaderCircle className="spin text-emerald-700" size={27} /><h2>{label}</h2><p>Estamos recuperando la información disponible.</p></div>
}
export function ErrorState({ error, retry, title = 'No pudimos cargar la información' }: { error: unknown; retry?: () => void; title?: string }) {
  return <div className="state-block" role="alert"><span className="state-icon error"><AlertCircle /></span><h2>{title}</h2><p>{errorMessage(error)}</p>
    {error instanceof ApiError && error.traceId && <small>Referencia de soporte: {error.traceId}</small>}
    {retry && <button className="button secondary" onClick={retry}>Volver a intentar</button>}</div>
}
export function PageHeading({ eyebrow, title, description, action }: { eyebrow?: string; title: string; description: string; action?: ReactNode }) {
  return <div className="page-heading"><div>{eyebrow && <p className="eyebrow">{eyebrow}</p>}<h1>{title}</h1><p>{description}</p></div>{action}</div>
}
export function BackLink({ to, children }: { to: string; children: ReactNode }) {
  return <Link className="text-link back-link" to={to}><ArrowLeft size={16} />{children}</Link>
}
