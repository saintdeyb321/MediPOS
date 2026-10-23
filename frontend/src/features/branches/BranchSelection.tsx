import { ArrowRight, MapPin } from 'lucide-react'
import { Link } from 'react-router'
import { ApiErrorState } from '../access'
import { BackLink } from '../../shared/ui/BackLink'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Notice } from '../../shared/ui/Notice'
import { PageHeader } from '../../shared/ui/PageHeader'
import { Panel } from '../../shared/ui/Panel'
import { EmptyState, LoadingState } from '../../shared/ui/States'
import { Steps } from '../../shared/ui/Steps'
import { useBranches } from './useBranches'

export function BranchSelection() {
  const { tenantId, valid, branches } = useBranches()
  let content
  if (!valid) content = <Notice tone="error">El identificador de botica no es válido.</Notice>
  else if (branches.isPending) content = <LoadingState label="Buscando tus sucursales…" />
  else if (branches.isError) content = <ApiErrorState error={branches.error} retry={() => void branches.refetch()} />
  else if (!branches.data.length) content = (
    <Panel><EmptyState title="No hay sucursales disponibles" description="El propietario debe crear una sucursal o asignarte una para continuar." icon={<MapPin />} action={<Button variant="secondary" onClick={() => void branches.refetch()}>Actualizar sucursales</Button>} /></Panel>
  )
  else content = (
    <div className="branch-list">
      {branches.data.map(branch => (
        <Link className="branch-option" to={`/boticas/${tenantId}/sucursales/${branch.id}`} key={branch.id}>
          <span className="state-icon"><MapPin aria-hidden="true" /></span>
          <div><h2>{branch.name}</h2><p>Inventario y caja independientes</p><span className="text-link">Seleccionar sucursal</span></div>
          <Badge>Sucursal</Badge>
          <ArrowRight size={22} aria-hidden="true" />
        </Link>
      ))}
    </div>
  )
  return (
    <>
      <BackLink to="/seleccionar-botica">Cambiar botica</BackLink>
      <Steps labels={['Botica', 'Sucursal', 'Tu espacio']} current={1} />
      <PageHeader eyebrow="ELIGE DÓNDE OPERAR" title="Selecciona una sucursal" description="Caja e inventario pertenecen a cada sucursal. El acceso y el horario se verifican al ingresar." />
      {content}
      {valid && <p><Link to={`/boticas/${tenantId}/configuracion`}>Configurar entidades y sucursales (solo Owner)</Link></p>}
    </>
  )
}
