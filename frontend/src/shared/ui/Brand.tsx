import { HeartPulse } from 'lucide-react'

export function Brand() {
  return (
    <span className="brand">
      <span className="brand-mark" aria-hidden="true"><HeartPulse size={26} strokeWidth={2.2} /></span>
      <span>Medi<span className="brand-accent">POS</span><small>Tu botica, en orden</small></span>
    </span>
  )
}
