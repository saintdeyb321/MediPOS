export function platformDestination(params: URLSearchParams, area: string, nextArea: string) {
  const next = new URLSearchParams(params)
  next.set('area', nextArea)
  if (area !== nextArea) {
    const legacyCursor = next.get('after')
    if (legacyCursor && area !== 'home') next.set('after-' + area, legacyCursor)
    next.delete('after')
    if (!next.has('platformPending') && !next.has('creationKey') && !next.has('categoryPending')) next.delete('tenant')
  }
  return '/plataforma?' + next.toString()
}
export const expiresSoon = (expiresAt: string, asOf: number) => {
  const end = Date.parse(expiresAt)
  return end >= asOf && end <= asOf + 7 * 86_400_000
}
export const ownerLabel = (status: string) => ({ pending: 'Owner pendiente', claimed: 'Asignación reclamada', active: 'Owner activo', inactive: 'Owner inactivo', revoked: 'Asignación revocada', unassigned: 'Requiere regularización' })[status] ?? 'Consultar propietario'
// Mirrors License's existing transitions for UI guidance; the server rechecks them.
export const licenseActions = (status: string) => ({ renew: ['trial', 'active', 'grace', 'suspended'].includes(status), suspend: ['trial', 'active', 'grace'].includes(status), reactivate: status === 'suspended' })
