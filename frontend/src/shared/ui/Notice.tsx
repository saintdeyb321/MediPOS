import type { ReactNode } from 'react'
import { AlertCircle, Info, TriangleAlert } from 'lucide-react'

export function Notice({ tone = 'info', children }: { tone?: 'info' | 'warning' | 'error'; children: ReactNode }) {
  const Icon = tone === 'error' ? AlertCircle : tone === 'warning' ? TriangleAlert : Info
  return (
    <div className={`notice ${tone}`} role={tone === 'info' ? 'note' : 'alert'}>
      <Icon size={20} aria-hidden="true" />
      <div>{children}</div>
    </div>
  )
}
