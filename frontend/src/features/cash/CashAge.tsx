import { useEffect, useState } from 'react'
import { Clock3 } from 'lucide-react'
import { Notice } from '../../shared/ui/Notice'
import { DateText } from '../../shared/ui/DateText'

// UI reminder only; it neither expires nor closes a cash session.
const reminderHours = 8
export function CashAge({ openedAt, compact = false }: { openedAt: string; compact?: boolean }) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 60_000); return () => window.clearInterval(timer) }, [])
  const start = Date.parse(openedAt)
  if (!Number.isFinite(start) || start > now) return <span className="data-caption">Antigüedad no disponible</span>
  const elapsed = Math.floor((now - start) / 60_000)
  if (compact) return <span className={elapsed >= reminderHours * 60 ? 'cash-age-warning' : 'data-caption'}>Abierta {Math.floor(elapsed / 60)} h {elapsed % 60} min{elapsed >= reminderHours * 60 && ' · revisa el cierre'}{elapsed >= 24 * 60 && ' · más de un día'}</span>
  return <div className="cash-age"><p><Clock3 size={16} aria-hidden="true" />Abierta hace {Math.floor(elapsed / 60)} h {elapsed % 60} min · <DateText value={openedAt} /></p>
    {elapsed >= reminderHours * 60 && <Notice tone="warning"><div><strong>{elapsed >= 24 * 60 ? 'Caja abierta durante más de un día' : 'Revisa el cierre de tu caja'}</strong><p>Recordatorio visual desde las {reminderHours} h; no cierra ni bloquea la sesión.</p></div></Notice>}
  </div>
}
