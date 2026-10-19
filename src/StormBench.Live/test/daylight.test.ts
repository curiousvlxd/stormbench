import { describe, expect, it } from 'vitest';
import { daylight, lampsOn, twilight } from '../src/world/daylight';

describe('daylight', () => {
  it('is day at noon and night at midnight, whatever the day', () => {
    expect(daylight(12)).toBe(1);
    expect(daylight(0)).toBe(0);
    expect(daylight(24 + 12)).toBe(1);
    expect(daylight(-1)).toBe(0);
  });

  it('rises through the dawn and falls through the dusk', () => {
    expect(daylight(5)).toBeGreaterThan(0);
    expect(daylight(5)).toBeLessThan(daylight(5.5));
    expect(daylight(21.5)).toBeGreaterThan(daylight(22));
  });

  it('glows warm only around sunrise and sunset', () => {
    expect(twilight(12)).toBeCloseTo(0);
    expect(twilight(0)).toBeCloseTo(0);
    expect(twilight(5.25)).toBeGreaterThan(0.5);
  });

  it('turns the lamps on before it is dark and off by day', () => {
    expect(lampsOn(0)).toBe(1);
    expect(lampsOn(1)).toBe(0);
    expect(lampsOn(0.5)).toBeGreaterThan(0);
  });
});
