import { Link } from 'react-router'
import { ArrowLeft } from 'lucide-react'
import { useWorkspace } from '../access'
import { PageHeader } from '../../shared/ui/PageHeader'

export function PosHeader() {
  const { base, branch } = useWorkspace()
  return <PageHeader title="Punto de venta" description={branch.name} action={<Link className="button secondary" to={base}><ArrowLeft size={18} aria-hidden="true" />Volver al inicio</Link>} />
}
