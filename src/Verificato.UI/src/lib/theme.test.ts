import { describe, expect, it } from 'vitest';
import { initialTheme, THEME_STORAGE_KEY } from './theme';

const storage = (values: Record<string, string>) => ({ getItem: (key: string) => values[key] ?? null });

describe('blue palette preferences', () => {
  it('defaults to dark without a saved choice', () => {
    expect(initialTheme(storage({}))).toBe('dark');
  });
  it('starts dark even when the old palette was light', () => {
    expect(initialTheme(storage({ 'verificato-theme': 'light' }))).toBe('dark');
  });
  it('preserves an explicit light choice for the blue palette', () => {
    expect(initialTheme(storage({ [THEME_STORAGE_KEY]: 'light' }))).toBe('light');
  });
  it('preserves an explicit dark choice', () => {
    expect(initialTheme(storage({ [THEME_STORAGE_KEY]: 'dark' }))).toBe('dark');
  });
  it('falls back to dark for invalid preferences', () => {
    expect(initialTheme(storage({ [THEME_STORAGE_KEY]: 'invalid' }))).toBe('dark');
  });
});
