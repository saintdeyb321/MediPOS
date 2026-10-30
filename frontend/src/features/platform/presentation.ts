export const ownerLabel = (status: string) => ({ pending: 'Owner pendiente', claimed: 'Owner activo', active: 'Owner activo', inactive: 'Owner inactivo', revoked: 'Asignación revocada', unassigned: 'Requiere regularización' })[status] ?? 'Consultar propietario'
// Mirrors License's existing transitions for UI guidance; the server rechecks them.
export const licenseActions = (status: string) => ({ renew: ['trial', 'active', 'grace', 'suspended'].includes(status), suspend: ['trial', 'active', 'grace'].includes(status), reactivate: status === 'suspended' })
