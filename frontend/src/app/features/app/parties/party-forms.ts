import { AbstractControl, ValidationErrors } from '@angular/forms';
import { normalizeDocument } from '../../../shared/documents';
import { DocumentType } from '../business.models';

/** valida el numero segun el tipo de documento del mismo formulario (mismas reglas que el backend) */
export function documentValidator(group: AbstractControl): ValidationErrors | null {
  const type = group.get('documentType')?.value as DocumentType | undefined;
  const number = String(group.get('documentNumber')?.value ?? '').trim();
  if (!type || number === '') return null;
  return normalizeDocument(type, number) ? null : { document: true };
}

/** texto opcional recortado (null si viene vacio) */
export const optional = (value: string): string | null => value.trim() || null;
