import { formatPrice, readableTextOn } from './format';
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
