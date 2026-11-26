import type { ReactNode } from 'react'

export function EditorLayout({ children, summary }: { children: ReactNode; summary?: ReactNode }) {
  return <div className="transaction-layout"><div className="transaction-grid">
    <div className="transaction-main">{children}</div>
    {summary && <aside className="transaction-summary">{summary}</aside>}
  </div></div>
}
