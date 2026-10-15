import type { ReactNode } from 'react'

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'success' | 'info' | 'warning'; children: ReactNode }) {
  return <span className={`badge ${tone}`}>{children}</span>
}
