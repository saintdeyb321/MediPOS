import { JsonNumber } from '../../shared/api/exactJson'
import type { CatalogItem, EventEnvelope } from './api'
import type { OfflineLease } from './storage'

// Synthetic contracts only; no production seed, OIDC identity or PostgreSQL evidence.
export const id = (n: number) => `019a0162-0000-7000-8000-${String(n).padStart(12, '0')}`
export const n = (value: string) => new JsonNumber(value)
export function lease(): OfflineLease { const now = Date.now(); return { userId: id(1), tenantId: id(2), branchId: id(3), membershipId: id(4), tenantName: 'Unit tenant', branchName: 'Unit branch', cashSessionId: id(5), preparedAt: now, lastObservedAt: now, expiresAt: now + 60_000, nonce: crypto.randomUUID(), cursor: 'unit-cursor', device: { deviceId: id(6), branchId: id(3), deviceLabel: 'Unit device', status: 'active', registeredAtUtc: new Date(now).toISOString(), revokedAtUtc: null } } }
export function item(version = '9007199254740993'): CatalogItem { return { businessProductId: id(7), version: n(version), operation: 'upsert',
  product: { businessProductId: id(7), internalCode: 'UNIT', name: 'Producto sintético unitario', productType: 'retail', categoryId: id(8), categoryName: 'Unidad', brandOrLaboratory: 'Unit', barcode: null, retailPrice: n('0.0001'), wholesalePrice: null, isActive: true, globalProductId: null, medicine: null,
    units: [{ productUnitId: id(9), name: 'Base', conversionToBase: n('1'), isBaseUnit: true, isActive: true }] }, stock: { physicalStockBase: n('9007199254740993.000000000001'), sellableStockBase: n('1'), expiredStockBase: n('0'), stockAsOfUtc: new Date().toISOString() } } }
export function envelope(): EventEnvelope { return { eventId: crypto.randomUUID(), deviceId: id(6), eventType: 'offline_sale.v1', schemaVersion: 1, clientOccurredAtUtc: new Date().toISOString(), clientLocalDate: '2026-10-17', clientTimeZoneId: 'America/Lima', payload: '{"unit":true}', payloadHash: 'a'.repeat(64) } }
