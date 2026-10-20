// mismas reglas que Nit.cs en el backend (algoritmo de la DIAN con pesos primos y modulo 11)
const WEIGHTS = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

/** digito de verificacion de un numero de NIT (solo digitos y sin el digito) */
export function nitCheckDigit(number: string): number {
  let sum = 0;
  for (let i = 0; i < number.length; i++) sum += Number(number[number.length - 1 - i]) * WEIGHTS[i];
  const remainder = sum % 11;
  return remainder > 1 ? 11 - remainder : remainder;
}

/** 900.123.456-8 o 900123456-8 da 900123456-8 (null si el formato no sirve o el digito no corresponde) */
export function normalizeNit(input: string): string | null {
  const parts = input.replace(/[.\s]/g, '').split('-');
  if (parts.length !== 2) return null;
  const [number, digit] = parts;
  if (!/^\d{6,15}$/.test(number) || !/^\d$/.test(digit)) return null;
  return nitCheckDigit(number) === Number(digit) ? `${number}-${digit}` : null;
}
