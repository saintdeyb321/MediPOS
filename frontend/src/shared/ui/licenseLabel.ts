const labels: Record<string, string> = {
  trial: 'Prueba gratuita', active: 'Activa', grace: 'Periodo de gracia', suspended: 'Suspendida',
  cancelled: 'Cancelada', purge_pending: 'Eliminación pendiente', purged: 'Eliminada',
}
export function licenseExpired(expiresAt?: string | null, serverTime?: string | null) {
  return Boolean(expiresAt && serverTime && Date.parse(expiresAt) <= Date.parse(serverTime))
}
export function licenseLabel(value: string, expiresAt?: string | null, serverTime?: string | null) {
  const label = labels[value] ?? value
  return licenseExpired(expiresAt, serverTime)
    ? `${label} · vencimiento superado` : label
}
