import { formatPrice, readableTextOn } from './format';
import { splitTax } from './taxes';
import { isPerson, normalizeDocument } from './documents';
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
