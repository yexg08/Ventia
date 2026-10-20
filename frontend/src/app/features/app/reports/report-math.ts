import { addDays } from '../../../shared/dates';

export type RangePreset = 'today' | 'week' | 'month' | 'lastMonth';

export const RANGE_PRESETS: { value: RangePreset; label: string }[] = [
  { value: 'today', label: 'Hoy' },
  { value: 'week', label: 'Últimos 7 días' },
  { value: 'month', label: 'Este mes' },
  { value: 'lastMonth', label: 'Mes pasado' },
];

/** rango de fechas yyyy-mm-dd de un atajo contado desde hoy */
export function presetRange(preset: RangePreset, today: string): { from: string; to: string } {
  const monthStart = today.slice(0, 8) + '01';
  switch (preset) {
    case 'today':
      return { from: today, to: today };
    case 'week':
      return { from: addDays(today, -6), to: today };
    case 'month':
      return { from: monthStart, to: today };
    case 'lastMonth': {
      const lastDay = addDays(monthStart, -1);
      return { from: lastDay.slice(0, 8) + '01', to: lastDay };
    }
  }
}

/** variacion porcentual frente al periodo anterior (null si antes no hubo ventas) */
export function percentChange(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return Math.round(((current - previous) / previous) * 1000) / 10;
}

/** porcentaje de una parte sobre el total con un decimal */
export function share(part: number, total: number): number {
  return total === 0 ? 0 : Math.round((part / total) * 1000) / 10;
}
