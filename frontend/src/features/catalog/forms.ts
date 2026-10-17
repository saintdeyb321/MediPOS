import { compare, decimalInput, maximumMoney, maximumQuantity } from '../../shared/api/decimal'
import { JsonNumber } from '../../shared/api/exactJson'
import type { MedicineInput, PricesInput, UnitInput } from './api'

export interface PriceFields { retail: string; wholesale: string }
export function prices(fields: PriceFields): PricesInput | null {
  const retailPrice = decimalInput(fields.retail, maximumMoney, 4, true)
  const wholesalePrice = fields.wholesale.trim() ? decimalInput(fields.wholesale, maximumMoney, 4, true) : null
  return retailPrice && (!fields.wholesale.trim() || wholesalePrice) ? { retailPrice, wholesalePrice } : null
}
export interface MedicineFields { components: { ingredient: string; strengthNormalized: string }[]; dosageForm: string; route: string; sanitaryRegistration: string }
export const emptyMedicine = (): MedicineFields => ({ components: [{ ingredient: '', strengthNormalized: '' }], dosageForm: '', route: '', sanitaryRegistration: '' })
export function medicineInput(fields: MedicineFields): MedicineInput | null {
  if (!fields.components.length || !required(fields.dosageForm, 128) || fields.route.trim().length > 128 || fields.sanitaryRegistration.trim().length > 128 ||
    fields.components.some(item => !required(item.ingredient, 200) || !required(item.strengthNormalized, 128))) return null
  return { components: fields.components.map(item => ({ ingredient: item.ingredient.trim(), strengthNormalized: item.strengthNormalized.trim() })),
    dosageForm: fields.dosageForm.trim(), route: fields.route.trim() || null, sanitaryRegistration: fields.sanitaryRegistration.trim() || null }
}
export const required = (value: string, length: number) => value.trim().length > 0 && value.trim().length <= length
export interface UnitFields { id?: string; name: string; conversion: string; base: boolean; active: boolean }
export function unitInputs(fields: UnitFields[]): UnitInput[] | null {
  if (fields.filter(row => row.base).length !== 1 || new Set(fields.map(row => row.name.trim())).size !== fields.length) return null
  const result: UnitInput[] = []
  for (const row of fields) {
    const conversion = decimalInput(row.conversion, maximumQuantity, 12)
    if (!required(row.name, 128) || !conversion || row.base && compare(conversion, new JsonNumber('1')) !== 0) return null
    result.push({ name: row.name.trim(), conversionToBase: conversion, isBaseUnit: row.base, isActive: row.active })
  }
  return result
}
