import type { RunState } from './types';
export const stateLabel: Record<RunState, string> = { Starting: 'Uruchamianie', Running: 'W trakcie', Stopping: 'Zatrzymywanie', Completed: 'Zakończono', Failed: 'Błąd wykonania', Interrupted: 'Przerwano' };
export const categoryLabel: Record<string, string> = { JavaScript: 'JavaScript', Console: 'Konsola', Http: 'HTTP', Resource: 'Zasoby', Performance: 'Wydajność', Compilation: 'Kompilacja', Analyzer: 'Analizator', CodeQuality: 'Jakość kodu', System: 'System' };
export const severityLabel = { Info: 'Informacja', Warning: 'Ostrzeżenie', Error: 'Błąd', Critical: 'Krytyczny' };
export function time(value: string): string { return new Date(value).toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
export function date(value: string): string { return new Date(value).toLocaleString('pl-PL', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }); }
export function number(value: number | null | undefined, suffix = ''): string { return value == null ? '—' : new Intl.NumberFormat('pl-PL', { maximumFractionDigits: 1 }).format(value) + suffix; }
export function duration(start: string, end?: string): string {
  const seconds = Math.max(0, Math.floor(((end ? new Date(end).getTime() : Date.now()) - new Date(start).getTime()) / 1000));
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
}
