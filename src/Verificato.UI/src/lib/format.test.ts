import { describe, expect, it, vi } from 'vitest';
import { duration, number, severityLabel, stateLabel } from './format';
describe('report formatting', () => {
  it('does not turn missing measurements into zero', () => {
    expect(number(undefined, ' ms')).toBe('—');
    expect(number(null)).toBe('—');
    expect(number(0, ' ms')).toBe('0 ms');
  });
  it('formats duration and clamps negative wall-clock differences', () => {
    expect(duration('2026-01-01T00:00:00Z', '2026-01-01T00:02:05Z')).toBe('02:05');
    expect(duration('2026-01-01T00:02:05Z', '2026-01-01T00:00:00Z')).toBe('00:00');
  });
  it('uses current time for active runs', () => {
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-01-01T00:01:30Z'));
    expect(duration('2026-01-01T00:00:00Z')).toBe('01:30');
    vi.restoreAllMocks();
  });
  it('renders statuses and severities as text, not color only', () => {
    expect(stateLabel.Interrupted).toBe('Przerwano');
    expect(severityLabel.Error).toBe('Błąd');
  });
});
