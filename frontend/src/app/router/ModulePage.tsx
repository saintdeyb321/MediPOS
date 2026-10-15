import { Link, useParams } from 'react-router'
import { ArrowLeft, Clock3, ShieldCheck } from 'lucide-react'
import { navigation } from '../navigation'
import { useWorkspace, roleLabels } from '../../features/access'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Badge } from '../../shared/ui/Badge'
import { Panel } from '../../shared/ui/Panel'
import { EmptyState } from '../../shared/ui/States'

export function ModulePage() {
  const { module } = useParams()
  const w = useWorkspace()
  const item = navigation.find(i => i.id === module)
  if (!item) return (
    <div className="state-block"><h1>Esta página no existe</h1><Link className="button secondary" to={w.base}>Ir al inicio</Link></div>
  )
  if (!item.roles.includes(w.access.role)) return (
    <Panel className="state-block" role="alert">
      <ShieldCheck size={28} aria-hidden="true" />
      <h1>Esta sección requiere otro acceso</h1>
      <p>Tu rol operacional actual no permite ver esta sección.</p>
      <Link className="button secondary" to={w.base}>Volver al inicio</Link>
    </Panel>
  )
  return (
    <>
      <PageHeader eyebrow="TU BOTICA" title={item.label} description={item.description} action={<Link className="button secondary" to={w.base}><ArrowLeft size={18} aria-hidden="true" />Volver al inicio</Link>} />
      {module === 'configuracion' ? <WorkspaceDetails /> : (
        <Panel>
          <div className="panel-heading"><Badge><Clock3 size={16} aria-hidden="true" />Próximamente</Badge></div>
          <EmptyState title={`Estamos preparando ${item.label.toLowerCase()}.`} description="Esta sección se conectará en una próxima entrega. Las operaciones de tu botica se mantendrán bajo los controles del servidor." icon={<item.icon size={28} />} action={<Link className="button secondary" to={w.base}>Volver a mi espacio</Link>} />
        </Panel>
      )}
    </>
  )
}

function WorkspaceDetails() {
  const w = useWorkspace()
  return (
    <Panel className="configuration-panel">
      <div className="panel-heading">
        <div><h2>Tu contexto de trabajo</h2><p>Información del acceso consultado. Esta vista no cambia la configuración.</p></div>
        <Badge tone="success">Solo lectura</Badge>
      </div>
      <dl className="configuration-list">
        <div><dt>Botica</dt><dd>{w.tenantName}</dd></div>
        <div><dt>Sucursal</dt><dd>{w.branch.name}</dd></div>
        <div><dt>Rol operacional</dt><dd>{roleLabels[w.access.role]}</dd></div>
        <div><dt>Acceso</dt><dd>Permitido en la última verificación</dd></div>
      </dl>
      <Link className="text-link" to={`/boticas/${w.tenantId}/sucursales`}>Elegir otra sucursal <ArrowLeft size={18} aria-hidden="true" /></Link>
    </Panel>
  )
}
