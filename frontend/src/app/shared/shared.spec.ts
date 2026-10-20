import { formatPrice, readableTextOn } from './format';
import { splitTax } from './taxes';
import { isPerson, normalizeDocument } from './documents';
import { cartTotals, changeFor, lineAmounts } from './cart';
import { addDays, localIsoDate } from './dates';
import { nitCheckDigit, normalizeNit } from './nit';

describe('nitCheckDigit (mismo algoritmo que Nit.cs en el backend)', () => {
  it.each([
    ['800197268', 4],
    ['900123456', 8],
    ['860002964', 4],
  ])('%s → %i', (number, expected) => {
    expect(nitCheckDigit(number)).toBe(expected);
  });
});

describe('normalizeNit', () => {
  it.each([
    ['800197268-4', '800197268-4'],
    ['800.197.268-4', '800197268-4'],
    [' 900123456 - 8 ', '900123456-8'],
  ])('%s → %s', (input, expected) => {
    expect(normalizeNit(input)).toBe(expected);
  });

  it.each(['800197268-5', '800197268', '12345-6', '80019726A-4', '800197268-44', ''])('rechaza "%s"', (input) => {
    expect(normalizeNit(input)).toBeNull();
  });
});

describe('formatPrice', () => {
  it('pesos colombianos sin decimales', () => {
    expect(formatPrice(18500).replace(/\s/g, ' ')).toBe('$ 18.500');
  });
});

describe('readableTextOn', () => {
  it('usa texto blanco sobre colores oscuros', () => {
    expect(readableTextOn('#1f2937')).toBe('#ffffff');
    expect(readableTextOn('#c2410c')).toBe('#ffffff');
  });

  it('usa texto oscuro sobre colores claros', () => {
    expect(readableTextOn('#f59e0b')).toBe('#111111');
  });
});

describe('splitTax (misma regla que Taxes.Split en el backend)', () => {
  it.each([
    [4000, 'Inc8', 3703.7, 296.3],
    [18000, 'Inc8', 16666.67, 1333.33],
    [11900, 'Iva19', 10000, 1900],
    [10500, 'Iva5', 10000, 500],
    [2500, 'None', 2500, 0],
  ] as const)('%i con %s', (gross, tax, base, taxAmount) => {
    expect(splitTax(gross, tax)).toEqual({ base, tax: taxAmount });
  });
});

describe('normalizeDocument (misma regla que Documents.cs en el backend)', () => {
  it.each([
    ['CC', '1.053.123.456', '1053123456'],
    ['CE', 'ab12345', 'AB12345'],
    ['PP', 'pa123456', 'PA123456'],
    ['NIT', '800.197.268-4', '800197268-4'],
  ] as const)('%s %s → %s', (type, input, expected) => {
    expect(normalizeDocument(type, input)).toBe(expected);
  });

  it.each([
    ['CC', '12'],
    ['CC', '12345678901'],
    ['CE', 'A1'],
    ['NIT', '800197268-5'],
  ] as const)('rechaza %s %s', (type, input) => {
    expect(normalizeDocument(type, input)).toBeNull();
  });

  it('solo las personas necesitan autorización de datos', () => {
    expect(isPerson('CC')).toBe(true);
    expect(isPerson('NIT')).toBe(false);
  });
});

describe('carrito (misma matemática que SalesService en el backend)', () => {
  it('calcula descuento, total y desglose por línea', () => {
    expect(lineAmounts({ unitPrice: 18000, quantity: 2, discountPercent: 10, tax: 'Inc8' })).toEqual({
      gross: 36000,
      discount: 3600,
      total: 32400,
      base: 30000,
      tax: 2400,
    });
  });

  it('suma el pedido igual que el servidor', () => {
    const totals = cartTotals([
      { unitPrice: 18000, quantity: 2, discountPercent: 10, tax: 'Inc8' },
      { unitPrice: 4000, quantity: 3, discountPercent: 0, tax: 'Inc8' },
    ]);
    expect(totals.total).toBe(44400);
    expect(totals.discountTotal).toBe(3600);
    expect(totals.subtotal + totals.taxTotal).toBeCloseTo(totals.total, 2);
  });

  it('calcula el cambio o avisa si el efectivo no alcanza', () => {
    expect(changeFor(44400, 50000)).toBe(5600);
    expect(changeFor(44400, 44400)).toBe(0);
    expect(changeFor(44400, 40000)).toBeNull();
  });
});

describe('fechas locales', () => {
  it('formatea y suma días sin moverse por la zona horaria', () => {
    expect(localIsoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
    expect(addDays('2026-12-25', 10)).toBe('2027-01-04');
  });
});
