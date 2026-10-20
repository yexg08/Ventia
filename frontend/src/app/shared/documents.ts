import { DocumentType } from '../features/app/business.models';
import { normalizeNit } from './nit';

/** misma regla que Documents.cs en el backend (devuelve el numero normalizado o null si no es valido) */
export function normalizeDocument(type: DocumentType, number: string): string | null {
  const compact = number.replace(/[.\s]/g, '');
  switch (type) {
    case 'CC':
      return /^\d{3,10}$/.test(compact) ? compact : null;
    case 'CE':
    case 'PP':
      return /^[A-Za-z0-9]{3,15}$/.test(compact) ? compact.toUpperCase() : null;
    case 'NIT':
      return normalizeNit(number);
  }
}

/** las personas deben autorizar el tratamiento de sus datos y las empresas con NIT no */
export const isPerson = (type: DocumentType): boolean => type !== 'NIT';

export function documentError(type: DocumentType): string {
  if (type === 'CC') return 'La cédula debe tener de 3 a 10 dígitos.';
  if (type === 'NIT') return 'Escribe el NIT con su dígito de verificación (revisa que el dígito sea correcto).';
  return 'El documento debe tener de 3 a 15 letras o números.';
}
