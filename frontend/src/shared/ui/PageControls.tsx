import { Button } from './Button'

export function PageControls({ offset, count, busy, change, refresh, size = 20 }: { offset: number; count?: number; busy: boolean; change: (offset: number) => void; refresh: () => void; size?: number }) {
  return <div className="operation-actions"><Button variant="quiet" disabled={busy} onClick={refresh}>Actualizar consulta</Button>
    <Button variant="quiet" disabled={busy || offset === 0} onClick={() => change(Math.max(0, offset - size))}>Página anterior</Button><span>Página {offset / size + 1}</span>
    <Button variant="quiet" disabled={busy || count !== size || offset + size > 10000} onClick={() => change(offset + size)}>Página siguiente</Button></div>
}
