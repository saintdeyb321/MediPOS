// @vitest-environment node
import { expect, it } from 'vitest'
import { JsonNumber } from '../../shared/api/exactJson'
import type { ProductMatch, ProductUnit } from '../catalog'
import { canSelectUnit, decimalLabel } from './productSelection'

const unit: ProductUnit = { id: 'unit', name: 'Unidad', isBaseUnit: true, isActive: true, conversionToBase: new JsonNumber('1') }
const product: ProductMatch = { businessProductId: 'product', name: 'Contrato unitario', productType: 'retail', brandOrLaboratory: '',
  internalCode: '', barcode: null, retailPrice: new JsonNumber('0'), wholesalePrice: null, quantityAvailableBase: new JsonNumber('1'), units: [unit] }

it.each([
  ['0', '1', false], ['-1', '1', false], ['1', '0', false], ['1', '-1', false],
  ['0.5', '1', false], ['1.0000', '1', true], ['0.0001', '1e-4', true],
  ['99999999999999.9998', '99999999999999.9999', false],
  ['99999999999999.9999', '99999999999999.9999', true],
])('checks stock %s against conversion %s without rounding', (stock, conversion, allowed) => {
  const presentation = { ...unit, conversionToBase: new JsonNumber(conversion) }
  expect(canSelectUnit({ ...product, units: [presentation], quantityAvailableBase: new JsonNumber(stock) }, presentation)).toBe(allowed)
})

it('rejects inactive, unrelated and unsupported presentations', () => {
  expect(canSelectUnit(product, { ...unit, isActive: false })).toBe(false)
  expect(canSelectUnit(product, { ...unit, id: 'not-returned' })).toBe(false)
  expect(canSelectUnit(product, { ...unit, conversionToBase: new JsonNumber('1e99') })).toBe(false)
})

it.each([
  ['99999999999999.9999', 2, '99999999999999.9999'], ['1', 2, '1.00'],
  ['0.5', 2, '0.50'], ['1e-4', 0, '0.0001'], ['1e2', 2, '100.00'],
])('displays %s exactly', (literal, digits, result) => {
  expect(decimalLabel(new JsonNumber(literal), digits)).toBe(result)
})
