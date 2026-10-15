import { ArrowLeft } from 'lucide-react'
import { Link } from 'react-router'
import type { ReactNode } from 'react'

export function BackLink({ to, children }: { to: string; children: ReactNode }) {
  return <Link className="text-link back-link" to={to}><ArrowLeft size={16} aria-hidden="true" />{children}</Link>
}
