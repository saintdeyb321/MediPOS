import { ChartNoAxesCombined } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export function MetricCard({ label, value, hint, icon: Icon = ChartNoAxesCombined, tone = 'primary' }: { label: string; value: string; hint?: string; icon?: LucideIcon; tone?: 'primary' | 'info' | 'success' | 'warning' }) {
  return <div className={`metric-card metric-${tone}`}><dt><span className="metric-icon"><Icon size={20} aria-hidden="true" /></span>{label}</dt><dd>{value}</dd>{hint && <small>{hint}</small>}</div>
}

export function MetricGrid({ metrics, compact = false }: { metrics: readonly (readonly [string, string])[]; compact?: boolean }) {
  return <dl className={`metric-grid${compact ? ' metric-grid-compact' : ''}`}>{metrics.map(([label, value], index) => <MetricCard key={label} label={label} value={value} tone={index === 0 ? 'primary' : 'info'} />)}</dl>
}
