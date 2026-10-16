import { ApiError } from '../../shared/api/http'
const messages: Record<string, string> = {
  'client.https_required': 'La conexión necesita HTTPS y el mismo origen que MediPOS. Contacta al administrador para configurar el acceso.',
  'client.timeout': 'El servidor no respondió a tiempo. Comprueba el estado antes de repetir una operación.',
  'client.outcome_unknown': 'No pudimos confirmar el resultado. Consulta el estado de la operación antes de volver a enviarla.',
  'client.precision_unsupported': 'Este navegador no puede conservar los decimales del servidor. Usa una versión actualizada.',
  'client.network_error': 'No pudimos conectar con MediPOS. Comprueba tu conexión y vuelve a intentarlo.',
  'client.unexpected_response': 'El servidor no devolvió una respuesta de MediPOS válida. Revisa la conexión y la configuración del sitio.',
  'client.invalid_sale_id': 'El identificador del borrador no es válido. Usa su dirección guardada o recupera el identificador correcto.',
  'cash_session.already_open': 'Ya tienes una caja abierta en esta sucursal. Consulta su estado antes de continuar.',
  'cash_session.invalid_opening_amount': 'El monto inicial debe ser no negativo y tener como máximo cuatro decimales.',
  'sale.concurrent_edit': 'Otra edición cambió el borrador. Compara el estado del servidor con tu carrito antes de guardar.',
  'sale.open_cash_session_required': 'Necesitas abrir tu caja en esta sucursal antes de crear o editar el borrador.',
  'sale.cash_session_mismatch': 'El borrador pertenece a otra sesión de caja. No puede editarse con la caja actual.',
  'sale.seller_required': 'Solo la membresía vendedora de este borrador puede editarlo o confirmar la venta.',
  'sale.not_draft': 'Este registro ya no es un borrador. La edición está bloqueada.',
  'sale.draft_not_found': 'No encontramos ese borrador en esta botica y sucursal.',
  'sale.invalid_line': 'Revisa la cantidad y la conversión de las presentaciones. No se redondean cantidades de stock.',
  'sale.invalid_payments': 'Usa importes positivos y exactos en los medios de pago admitidos.',
  'sale.duplicate_payment_method': 'Cada medio de pago debe aparecer una sola vez.',
  'sale.payment_total_mismatch': 'Los pagos deben sumar exactamente el total guardado de la venta.',
  'inventory.insufficient_stock': 'El stock disponible ya no alcanza. Revisa las cantidades antes de confirmar.',
  'sale.inconsistent_draft': 'El borrador no es consistente. Consulta su estado antes de continuar.',
  'sale.checkout_window_changed': 'Cambió la fecha operacional durante el envío. Consulta y revisa el resultado antes de confirmar otra vez.',
  'sale.product_unavailable': 'Un producto dejó de estar disponible. Revisa el catálogo antes de guardar.',
  'sale.unit_unavailable': 'Una presentación dejó de estar disponible. Busca el producto y revisa sus presentaciones actuales.',
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
  if (error instanceof ApiError && error.outcomeUnknown) return messages['client.outcome_unknown']
  return error instanceof ApiError ? messages[error.code] ?? ({
    401: messages['auth.session_required'], 403: 'No tienes autorización para esta operación.',
    409: 'El estado cambió. Consulta los datos actuales antes de continuar.',
    429: 'Has realizado varias solicitudes. Espera un momento antes de continuar.',
  }[error.status] ?? (error.status >= 500 ? 'El servicio no está disponible. Consulta el estado antes de repetir una operación.' : 'No pudimos completar la solicitud.')) : 'Ocurrió un problema inesperado. Vuelve a intentarlo.'
}
export const accessMessage = (code: string) => messages[code] ?? 'No tienes acceso operacional en este momento. Consulta al propietario.'
