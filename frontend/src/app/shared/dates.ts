/** fecha local en formato yyyy-mm-dd (la de los campos de tipo date) */
export function localIsoDate(date = new Date()): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** suma dias a una fecha yyyy-mm-dd */
export function addDays(isoDate: string, days: number): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  return localIsoDate(new Date(y, m - 1, d + days));
}

/** 2026-10-20 se muestra como 20 oct 2026 */
export function formatIsoDate(isoDate: string): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  return new Intl.DateTimeFormat('es-CO', { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(y, m - 1, d));
}
