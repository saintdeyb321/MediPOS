import { Button } from './Button'

export function PageControls({ offset, count, busy, change, size = 20 }: { offset: number; count?: number; busy: boolean; change: (offset: number) => void; size?: number }) {
  const canAdvance = count === size && offset + size <= 10_000
  if (offset === 0 && !canAdvance) return null
  return <nav className="operation-actions page-controls" aria-label="Paginación">
    {offset > 0 && <Button variant="quiet" disabled={busy} onClick={() => change(Math.max(0, offset - size))}>Página anterior</Button>}
    <span aria-current="page">Página {offset / size + 1}</span>
    {canAdvance && <Button variant="quiet" disabled={busy} onClick={() => change(offset + size)}>Página siguiente</Button>}
  </nav>
}
