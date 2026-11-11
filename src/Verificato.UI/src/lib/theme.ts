export type Theme = 'dark' | 'light';

// Start the new blue palette in dark mode, including installations of the previous green palette.
// Subsequent explicit choices are preserved without changing the old preference.
export const THEME_STORAGE_KEY = 'verificato-theme-blue-v1';

export function initialTheme(storage: Pick<Storage, 'getItem'>): Theme {
  return storage.getItem(THEME_STORAGE_KEY) === 'light' ? 'light' : 'dark';
}
