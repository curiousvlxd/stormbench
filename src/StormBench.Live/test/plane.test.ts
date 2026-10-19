import { describe, expect, it } from 'vitest';
import { angleGap, distanceToPolygonEdge, distanceToSegment, pointInPolygon } from '../src/math/plane';

const square = [
  { x: 0, z: 0 },
  { x: 4, z: 0 },
  { x: 4, z: 4 },
  { x: 0, z: 4 }
];

describe('pointInPolygon', () => {
  it('finds points inside and outside a square', () => {
    expect(pointInPolygon(2, 2, square)).toBe(true);
    expect(pointInPolygon(5, 2, square)).toBe(false);
    expect(pointInPolygon(-0.1, 3, square)).toBe(false);
  });

  it('handles a concave polygon', () => {
    const ell = [
      { x: 0, z: 0 },
      { x: 4, z: 0 },
      { x: 4, z: 1 },
      { x: 1, z: 1 },
      { x: 1, z: 4 },
      { x: 0, z: 4 }
    ];
    expect(pointInPolygon(0.5, 3, ell)).toBe(true);
    expect(pointInPolygon(3, 3, ell)).toBe(false);
  });
});

describe('distanceToSegment', () => {
  it('measures to the nearest point of the segment', () => {
    const a = { x: 0, z: 0 };
    const b = { x: 10, z: 0 };
    expect(distanceToSegment({ x: 5, z: 3 }, a, b)).toBeCloseTo(3);
    expect(distanceToSegment({ x: -3, z: 4 }, a, b)).toBeCloseTo(5);
    expect(distanceToSegment({ x: 13, z: -4 }, a, b)).toBeCloseTo(5);
  });

  it('treats a zero-length segment as a point', () => {
    const p = { x: 1, z: 1 };
    expect(distanceToSegment({ x: 4, z: 5 }, p, p)).toBeCloseTo(5);
  });
});

describe('distanceToPolygonEdge', () => {
  it('is the distance to the closest edge, inside or out', () => {
    expect(distanceToPolygonEdge({ x: 1, z: 2 }, square)).toBeCloseTo(1);
    expect(distanceToPolygonEdge({ x: 6, z: 2 }, square)).toBeCloseTo(2);
  });
});

describe('angleGap', () => {
  it('wraps round the circle', () => {
    expect(angleGap(0.1, -0.1)).toBeCloseTo(0.2);
    expect(angleGap(Math.PI - 0.1, -Math.PI + 0.1)).toBeCloseTo(0.2);
    expect(angleGap(0, Math.PI)).toBeCloseTo(Math.PI);
  });
});
