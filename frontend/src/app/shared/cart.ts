import { TaxType } from '../features/app/business.models';
import { splitTax } from './taxes';

/** linea del carrito (lo minimo para calcular totales igual que el servidor) */
export interface CartLineMath {
  unitPrice: number;
  quantity: number;
  discountPercent: number;
  tax: TaxType;
}

export interface LineAmounts {
  gross: number;
  discount: number;
  total: number;
  base: number;
  tax: number;
}

export interface CartTotals {
  subtotal: number;
  taxTotal: number;
  discountTotal: number;
  total: number;
}

function round2(value: number): number {
  const sign = Math.sign(value);
  return (sign * Math.round(Math.abs(value) * 100 + Number.EPSILON)) / 100;
}

/** misma regla que el servidor (descuento redondeado a 2 decimales y desglose con Taxes.Split) */
export function lineAmounts(line: CartLineMath): LineAmounts {
  const gross = round2(line.unitPrice * line.quantity);
  const discount = round2((gross * line.discountPercent) / 100);
  const total = round2(gross - discount);
  const split = splitTax(total, line.tax);
  return { gross, discount, total, base: split.base, tax: split.tax };
}

/** totales del pedido (solo para mostrar y el comprobante trae los del servidor) */
export function cartTotals(lines: CartLineMath[]): CartTotals {
  return lines.map(lineAmounts).reduce<CartTotals>(
    (acc, l) => ({
      subtotal: round2(acc.subtotal + l.base),
      taxTotal: round2(acc.taxTotal + l.tax),
      discountTotal: round2(acc.discountTotal + l.discount),
      total: round2(acc.total + l.total),
    }),
    { subtotal: 0, taxTotal: 0, discountTotal: 0, total: 0 },
  );
}

/** cambio a devolver (null si el efectivo no alcanza) */
export function changeFor(cashAmount: number, tendered: number): number | null {
  return tendered >= cashAmount ? round2(tendered - cashAmount) : null;
}
