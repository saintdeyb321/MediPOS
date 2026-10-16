// @vitest-environment node
import { expect, it } from 'vitest'
import { JsonNumber } from '../../shared/api/exactJson'
import { decimalInput, maximumMoney, maximumQuantity, roundEven4 } from '../../shared/api/decimal'
import { addSelection, cartError, estimateTotal, lineAmounts, saleInputs } from './cart'
import type { ProductMatch } from '../catalog'

const n = (literal: string) => new JsonNumber(literal)
const product: ProductMatch = { businessProductId: 'product', name: 'Prueba unitaria', internalCode: '', barcode: null, productType: 'retail', brandOrLaboratory: '',
  retailPrice: n('0.1'), wholesalePrice: null, quantityAvailableBase: n('20'), units: [
    { id: 'tablet', name: 'Tableta', conversionToBase: n('1'), isBaseUnit: true, isActive: true },
    { id: 'box', name: 'Caja', conversionToBase: n('10'), isBaseUnit: false, isActive: true },
  ] }
it('keeps two presentations, accumulates exact quantities, and sums base consumption across lines', () => {
  const one = addSelection([], { product, unit: product.units[0]!, quantity: n('0.1') })
  const two = addSelection(one, { product, unit: product.units[0]!, quantity: n('0.2') })
  const lines = addSelection(two, { product, unit: product.units[1]!, quantity: n('1') })
  expect(lines).toHaveLength(2)
  expect(two[0]!.quantity).toBe('0.3')
  expect(one[0]!.quantity).toBe('0.1')
  expect(lineAmounts(lines[1]!)?.base.literal).toBe('10')
  expect(lineAmounts(lines[1]!)?.unitPrice.literal).toBe('1.0')
  expect(estimateTotal(lines).literal).toBe('1.03')
  expect(saleInputs(lines)[0]?.quantity.literal).toBe('0.3')
  expect(() => addSelection(lines, { product, unit: product.units[1]!, quantity: n('1') })).toThrow(/stock/)
})
it('rejects duplicate presentations after edits and invalid or inexact quantities without rounding', () => {
  const lines = addSelection([], { product, unit: product.units[0]! })
  expect(cartError([lines[0]!, { ...lines[0]! }])).toMatch(/ya está/)
  for (const quantity of ['', '0', '-1', '1e2', 'NaN', '0.0000000000001']) expect(cartError([{ ...lines[0]!, quantity }])).toBeTruthy()
  expect(cartError([{ ...lines[0]!, quantity: '21' }])).toMatch(/stock/)
  const tiny = { ...lines[0]!, quantity: '0.000000000001', units: [{ ...product.units[0]!, conversionToBase: n('0.1') }] }
  expect(lineAmounts(tiny)).toBeNull()
})
it('keeps independent products when adding subsequent selections', () => {
  const first = addSelection([], { product, unit: product.units[0]! })
  const second = { ...product, businessProductId: 'other-product', name: 'Otra prueba' }
  const both = addSelection(first, { product: second, unit: second.units[0]! })
  expect(both.map(line => line.productId)).toEqual(['product', 'other-product'])
  expect(first).toHaveLength(1)
})
it('preserves large monetary precision and rejects overflow', () => {
  const large = { ...product, retailPrice: n('99999999999999.9999') }
  const lines = addSelection([], { product: large, unit: large.units[0]! })
  expect(estimateTotal(lines).literal).toBe('99999999999999.9999')
  expect(() => addSelection(lines, { product: large, unit: large.units[0]! })).toThrow(/importe|representables/)
})
it.each([['0.00025', '0.0002'], ['0.00035', '0.0004'], ['-0.00025', '-0.0002'], ['-0.00035', '-0.0004']])('estimates to-even %s without binary arithmetic', (input, expected) => {
  expect(roundEven4(n(input)).literal).toBe(expected)
})
it('accepts exact decimal input within the existing cash/quantity contracts', () => {
  expect(decimalInput('0,50', maximumMoney, 4, true)?.literal).toBe('0.50')
  expect(decimalInput('0', maximumMoney, 4, true)?.literal).toBe('0')
  expect(decimalInput('0', maximumQuantity, 12)).toBeNull()
  expect(decimalInput('1.00001', maximumMoney, 4, true)).toBeNull()
  expect(decimalInput('100000000000000', maximumMoney, 4, true)).toBeNull()
})
