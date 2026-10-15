import { ApiError } from './http'
const messages: Record<string, string> = {
  'client.https_required': 'La conexión real necesita HTTPS y el mismo origen que MediPOS. Puedes explorar la vista de desarrollo mientras se configura.',
  'client.network_error': 'No pudimos conectar con MediPOS. Comprueba tu conexión y vuelve a intentarlo.',
  'client.unexpected_response': 'El servidor no devolvió una respuesta de MediPOS válida. Revisa la conexión y la configuración del sitio.',
  'auth.session_required': 'Tu sesión terminó. Vuelve a ingresar con Google para continuar.',
  'auth.csrf_invalid': 'No se pudo verificar esta solicitud. Actualiza la página e inténtalo de nuevo.',
  'auth.origin_denied': 'Este sitio no está autorizado para conectar con MediPOS. Contacta al administrador.',
  'access.license_denied': 'La licencia de esta botica no permite operar. Comunícate con el propietario.',
  'access.schedule_denied': 'Estás fuera de tu horario autorizado para esta sucursal.',
  'access.branch_unassigned': 'No tienes asignada esta sucursal. Elige otra o contacta al propietario.',
  'access.branch_denied': 'Esta sucursal no está disponible para tu botica.',
  'access.membership_inactive': 'Tu acceso a esta botica fue desactivado. Contacta al propietario.',
  'access.membership_missing': 'No tienes una membresía vigente en esta botica.',
  'access.role_denied': 'Tu rol actual no permite acceder a esta sección.',
  'access.context_changed': 'Tu acceso cambió. Vuelve a seleccionar una sucursal.',
  'api.rate_limited': 'Has realizado varias solicitudes. Espera un momento y vuelve a intentarlo.',
}
export function errorMessage(error: unknown) {
  return error instanceof ApiError ? messages[error.code] ?? (error.status === 401 ? messages['auth.session_required'] : 'No pudimos completar la solicitud. Vuelve a intentarlo.') : 'Ocurrió un problema inesperado. Vuelve a intentarlo.'
}
export const accessMessage = (code: string) => messages[code] ?? 'No tienes acceso operacional en este momento. Consulta al propietario.'
