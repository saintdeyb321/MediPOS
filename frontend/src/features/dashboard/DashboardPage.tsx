import { ArrowUpRight, LayoutDashboard } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useWorkspace } from '../access'
import { PageHeader } from '../../shared/ui/PageHeader'
import type { Availability } from '../../shared/ui/availability'
import './dashboard.css'

export interface DashboardShortcut { id: string; label: string; description: string; icon: LucideIcon; availability: Availability }

export function DashboardPage({ shortcuts, metrics }: { shortcuts: DashboardShortcut[]; metrics?: ReactNode }) {
  const w = useWorkspace()
  return <section className="home-compact"><PageHeader icon={LayoutDashboard} title="Inicio" description={w.branch.name} />
    <nav className="home-actions" aria-label="Tareas frecuentes">{shortcuts.map(item => <Link key={item.id} to={`${w.base}/${item.id}`} className={`home-action${item.id === 'ventas' ? ' home-action-primary' : ''}`}>
      <item.icon size={22} aria-hidden="true" /><strong>{item.label}</strong><ArrowUpRight size={17} aria-hidden="true" />
    </Link>)}</nav>
    {metrics}
  </section>
}