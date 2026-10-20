import { TaxType } from '../features/app/business.models';

const RATES: Record<TaxType, number> = { None: 0, Iva5: 0.05, Iva19: 0.19, Inc8: 0.08 };

/** redondeo a 2 decimales alejandose de cero (igual que MidpointRounding.AwayFromZero en el backend) */
function round2(value: number): number {
  const sign = Math.sign(value);
  return (sign * Math.round(Math.abs(value) * 100 + Number.EPSILON)) / 100;
}

/** misma regla que Taxes.Split en el backend (solo para mostrar mientras se escribe y la API tiene la ultima palabra) */
export function splitTax(grossPrice: number, tax: TaxType): { base: number; tax: number } {
  const rate = RATES[tax];
  if (rate === 0) return { base: grossPrice, tax: 0 };
  const base = round2(grossPrice / (1 + rate));
  return { base, tax: round2(grossPrice - base) };
}
