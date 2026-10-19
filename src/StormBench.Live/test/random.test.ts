import { describe, expect, it } from 'vitest';
import { lehmer, random, townRandom } from '../src/math/random';

describe('random', () => {
  it('repeats the same sequence for the same seed', () => {
    const a = random(42);
    const b = random(42);
    expect(Array.from({ length: 5 }, a)).toEqual(Array.from({ length: 5 }, b));
  });

  it('draws in [0, 1)', () => {
    const r = random(7);
    for (let i = 0; i < 1000; i++) {
      const v = r();
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(1);
    }
  });

  it('starts skip draws into the sequence', () => {
    const full = random(20000530);
    for (let i = 0; i < 1234; i++) full();
    const skipped = random(20000530, 1234);
    expect(Array.from({ length: 10 }, skipped)).toEqual(Array.from({ length: 10 }, full));
  });

  it('skips past the draws the old texture pass took', () => {
    const full = random(20000530);
    for (let i = 0; i < 227218; i++) full();
    expect(townRandom()()).toBe(full());
  });
});

describe('lehmer', () => {
  it('is the minimal standard generator', () => {
    const r = lehmer(1);
    expect(r() * 2147483647).toBe(16807);
    expect(r() * 2147483647).toBeCloseTo(282475249, 3);
  });
});
