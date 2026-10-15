import { Link, useParams } from 'react-router'
import { ArrowLeft, Clock3, ShieldCheck } from 'lucide-react'
import { navigation } from '../../app/navigation'
import { useWorkspace } from '../../hooks/useSession'
import { PageHeading } from '../../components/ui'
import { roleLabels } from '../../types/contracts'
export function ModulePage() {
  const { module } = useParams(); const w = useWorkspace()
  const item = navigation.find(i => i.id === module)
  if (!item) return <div className="state-block"><h1>Esta página no existe</h1><Link className="button secondary" to={w.base}>Ir al inicio</Link></div>
  if (!item.roles.includes(w.access.role)) return <div className="panel state-block" role="alert"><ShieldCheck size={28} /><h1>Esta sección requiere otro acceso</h1><p>Tu rol operacional actual no permite ver esta sección.</p><Link className="button secondary" to={w.base}>Volver al inicio</Link></div>
  return <><PageHeading eyebrow="TU BOTICA" title={item.label} description={item.description} action={<Link className="button secondary" to={w.base}><ArrowLeft size={16} /> Volver al inicio</Link>} />
    {module === 'configuracion' ? <section className="panel configuration-panel"><div className="panel-heading"><div><h2>Tu contexto de trabajo</h2><p>Información del acceso consultado. Esta vista no cambia la configuración.</p></div><span className="badge green">Solo lectura</span></div><dl className="configuration-list"><div><dt>Botica</dt><dd>{w.tenantName}</dd></div><div><dt>Sucursal</dt><dd>{w.branch.name}</dd></div><div><dt>Rol operacional</dt><dd>{roleLabels[w.access.role]}</dd></div><div><dt>Acceso</dt><dd>Permitido en la última verificación</dd></div></dl><Link className="text-link" to={`/boticas/${w.tenantId}/sucursales`}>Elegir otra sucursal <ArrowLeft size={15} /></Link></section> : <section className="panel state-block module-placeholder"><span className="state-icon"><item.icon size={28} /></span><span className="badge neutral"><Clock3 size={13} /> Próximamente</span><h2>Estamos preparando {item.label.toLowerCase()}.</h2><p>Esta sección se conectará en una próxima entrega. Las operaciones de tu botica se mantendrán bajo los controles del servidor.</p><Link className="button secondary" to={w.base}>Volver a mi espacio</Link></section>}</>
}
