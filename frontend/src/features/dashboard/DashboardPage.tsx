import { ArrowRight, Clock3, MapPin, ShieldCheck } from 'lucide-react'
import { Link } from 'react-router'
import { useRuntime, useWorkspace, roleLabels } from '../access'
import { Badge } from '../../shared/ui/Badge'
import { Panel } from '../../shared/ui/Panel'
import { PageHeader } from '../../shared/ui/PageHeader'
import type { LucideIcon } from 'lucide-react'
import './dashboard.css'

export interface DashboardShortcut { id: string; label: string; description: string; icon: LucideIcon }

export function DashboardPage({ shortcuts }: { shortcuts: DashboardShortcut[] }) {
  const w = useWorkspace()
  const { preview } = useRuntime()
  const verified = new Intl.DateTimeFormat('es-PE', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Lima' }).format(new Date(w.access.evaluatedAtUtc))
  return (
    <>
      <PageHeader eyebrow={w.tenantName} title="Todo listo para empezar." description="Revisa tu sucursal y elige dónde continuar." />
      <Panel className="dashboard-action">
        <div><Badge tone="info">Vista inicial · F1.1</Badge><h2>Punto de venta</h2><p>Abre tu espacio de atención. La búsqueda y el cobro se habilitarán en F2.</p></div>
        <Link className="button primary" to={`${w.base}/ventas`}>Abrir punto de venta <ArrowRight size={20} aria-hidden="true" /></Link>
      </Panel>
      <dl className="workspace-summary">
        <div><dt><MapPin aria-hidden="true" />Sucursal seleccionada</dt><dd>{w.branch.name}</dd></div>
        <div><dt><ShieldCheck aria-hidden="true" />Acceso actual</dt><dd>{roleLabels[w.access.role]}<Badge tone={preview ? 'warning' : 'success'}>{preview ? 'Simulado' : 'Verificado'}</Badge></dd></div>
        <div><dt><Clock3 aria-hidden="true" />Última verificación · Lima</dt><dd><time dateTime={w.access.evaluatedAtUtc}>{verified}</time></dd></div>
      </dl>
      <Panel className="module-panel">
        <div className="panel-heading"><div><h2>Tu espacio de trabajo</h2><p>Secciones visibles según tu acceso actual.</p></div></div>
        <div className="module-rows">
          {shortcuts.map(item => (
            <Link key={item.id} to={`${w.base}/${item.id}`} className="module-row">
              <span className="module-icon"><item.icon size={22} aria-hidden="true" /></span>
              <span><strong>{item.label}</strong><small>{item.description}</small></span>
              <Badge>{item.id === 'configuracion' ? 'Ver contexto' : 'Próximamente'}</Badge>
              <ArrowRight size={18} aria-hidden="true" />
            </Link>
          ))}
        </div>
      </Panel>
      <Panel className="activity-panel">
        <div><h2>Indicadores de la sucursal</h2><p>Las ventas, caja y movimientos se mostrarán cuando sus consultas estén conectadas.</p></div>
        <Badge>Sin datos consultados</Badge>
      </Panel>
    </>
  )
}
