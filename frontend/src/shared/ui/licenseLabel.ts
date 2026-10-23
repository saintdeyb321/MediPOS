const labels: Record<string, string> = {
  trial: 'Prueba gratuita', active: 'Activa', grace: 'Periodo de gracia', suspended: 'Suspendida',
  cancelled: 'Cancelada', purge_pending: 'Eliminación pendiente', purged: 'Eliminada',
}
export const licenseLabel = (value: string) => labels[value] ?? value
