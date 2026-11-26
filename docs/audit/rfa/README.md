# Radiografía AS-IS · Fase 01

Baseline 7d453285252d2bf9279cb7c42e1ac95f4f8c70c6, rama ci/postgres-diagnostics-8bc6b4c, 26/11/2026 Lima. Diff inicial de 46 archivos (+665/−301), 11 sin rastrear y huella 664b8f1f7bb89ad58e3983f72ffc061e5529435d. Candidato UAX local con HTML SHA-256 68cb1c38d5fbe3a07d59479aae3d0e2772215b0194baab178797b825f8d429cf. Build servido y esquema real NO_DETERMINADO. SPEC, ARCHITECTURE y ADR son canónicos. El maestro histórico aporta intención. Se leyó la fuente oficial MIT de ub-mode y se continuó con el método del encargo porque la versión inmutable no pudo verificarse para instalación.

[Inventario](USE_CASES.md) · [Matriz](MATRIX.csv) · [Indicios](FINDINGS.md) · [Continuidad](CHECKPOINT.md)

## Cobertura

**FASE_01_PARCIAL**. Las 27 declaraciones inventariadas del router tienen una ruta hacia casos o una exclusión. 123 rutas HTTP declaradas tienen una ficha, incluidas admisión, callback y salud. El registro contiene 154 acciones/casos, de los que 80 tienen E1 y 74 siguen E0 en al menos una frontera. Una etiqueta E1 indica la trayectoria de código documentada en la ficha. Toda operación queda NOT_RUN para esta fase. Los tests se citan como fuentes, sin importar resultados históricos.

| Familia | E1 / inventario | IDs cortos E0 |
|---|---:|---|
| [Caja/POS](flows/01-caja-pos.md) | 18/18 | — |
| [Catálogo](flows/02-catalogo.md) | 10/22 | 08, 09, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 |
| [Compras e inventario](flows/03-compras-inventario.md) | 11/15 | 06, 08, 10, 14 |
| [Transferencias](flows/04-transferencias.md) | 10/13 | 02, 03, 04 |
| [Reportes y comisiones](flows/05-reportes-comisiones.md) | 11/16 | 01, 02, 03, 04, 05 |
| [Avisos/auditoría](flows/06-avisos-auditoria.md) | 5/7 | 01, 06 |
| [Acceso y estructura](flows/07-acceso-personal.md) | 8/27 | 01, 03, 04, 05, 06, 07, 08, 09, 10, 11, 15, 16, 17, 18, 19, 20, 21, 22, 23 |
| [Plataforma](flows/08-plataforma.md) | 5/18 | 01, 03, 04, 05, 06, 07, 10, 11, 12, 13, 15, 16, 18 |
| [Contingencia e integraciones](flows/09-contingencia-integraciones.md) | 2/18 | 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14, 16, 17, 18 |

## Dependencias funcionales

- Identidad, membresía, licencia y sede habilitan el espacio de trabajo. La autoridad de plataforma es independiente y la designación del primer Owner vincula ambos dominios mediante el login verificado.
- Catálogo y presentaciones alimentan POS, Compras y Transferencias. La recepción genera lotes y movimientos, y la confirmación de venta consume stock y produce pagos. La anulación compensa con registros auditados.
- Caja calcula efectivo físico desde pagos/reversiones y préstamos. El arqueo por ID y el directorio de abiertas son consultas distintas. Reportes agregan ventas/stock; comisiones contabilizan con la venta y su reversión.
- Avisos derivan de lotes y registran lectura por membresía. La auditoría muestra metadatos. El módulo offline usa catálogo de sede, dispositivo y acuses originales, y CPE queda desacoplado.
