// @vitest-environment node
import { expect, it } from 'vitest'
import { medicineInput, prices, unitInputs } from './forms'
import { stringifyExactJson } from '../../shared/api/exactJson'

it('preserves exact nonnegative prices and explicit wholesale removal', () => {
  expect(stringifyExactJson(prices({ retail: '99999999999999.9999', wholesale: '' }))).toBe('{"retailPrice":99999999999999.9999,"wholesalePrice":null}')
  expect(prices({ retail: '0', wholesale: '0' })).not.toBeNull()
  for (const retail of ['-1', '1e2', '0.00001', '100000000000000']) expect(prices({ retail, wholesale: '' })).toBeNull()
})
it('keeps documented medicine components and Unicode without dose conversion or clinical inference', () => {
  const input = { components: [{ ingredient: 'Ingrediente sintético de prueba', strengthNormalized: '25 µg' }], dosageForm: 'Forma de prueba', route: '', sanitaryRegistration: '' }
  expect(medicineInput(input)).toEqual({ ...input, route: null, sanitaryRegistration: null })
  expect(medicineInput({ ...input, components: [] })).toBeNull()
  expect(medicineInput({ ...input, components: [{ ingredient: '', strengthNormalized: '25 µg' }] })).toBeNull()
  expect(medicineInput({ ...input, dosageForm: 'x'.repeat(129) })).toBeNull()
})
it('requires exactly one base, exact conversion and names unique under the backend ordinal rule', () => {
  const base = { name: 'Unidad', conversion: '1', base: true, active: true }
  const box = { name: 'Caja', conversion: '10', base: false, active: true }
  expect(stringifyExactJson(unitInputs([base, box]))).toContain('"conversionToBase":10')
  for (const rows of [[], [box], [base, base], [{ ...base, conversion: '2' }], [base, { ...box, name: ' Unidad ' }], [base, { ...box, conversion: '0' }], [base, { ...box, conversion: '0.0000000000001' }]]) expect(unitInputs(rows)).toBeNull()
  expect(unitInputs([base, { ...box, name: 'unidad' }])).not.toBeNull()
})
