import { Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { nearestWater, stripEdges, waterPath } from '../src/world/water';

const straight = () => waterPath('River', [new Vector3(-50, 0, 0), new Vector3(50, 0, 0)], 6);

describe('waterPath', () => {
  it('samples the line evenly and measures its length', () => {
    const w = straight();
    expect(w.pts.length).toBe(201);
    expect(w.total).toBeCloseTo(100, 6);
    expect(w.cum[100]).toBeCloseTo(50, 2);
    expect(w.width).toBe(6);
  });
});

describe('nearestWater', () => {
  it('finds the bank, the centre line and the way along', () => {
    const w = straight();
    const hit = nearestWater(new Vector3(25, 0, 10), [w]);
    expect(hit.water).toBe(w);
    expect(hit.distance).toBeCloseTo(7);
    expect(hit.centre.x).toBeCloseTo(25);
    expect(hit.point.z).toBeCloseTo(3);
    expect(hit.u).toBeCloseTo(0.75);
    expect(Math.abs(hit.normal.z)).toBeCloseTo(1);
  });

  it('is negative inside the water and picks the nearer of two', () => {
    const river = straight();
    const creek = waterPath('Creek', [new Vector3(0, 0, -40), new Vector3(0, 0, -10)], 2);
    expect(nearestWater(new Vector3(10, 0, 1), [river, creek]).distance).toBeCloseTo(-2);
    expect(nearestWater(new Vector3(2, 0, -25), [river, creek]).water).toBe(creek);
  });
});

describe('stripEdges', () => {
  it('runs both edges half a width either side of the line', () => {
    const { left, right } = stripEdges(straight().pts, 3);
    for (const [l, r] of left.map((l, i) => [l, right[i]!] as const)) expect(l.distanceTo(r)).toBeCloseTo(6);
  });

  it('never lets an edge run backwards on the inside of a tight bend', () => {
    const bend = waterPath('Bend', [new Vector3(-10, 0, 0), new Vector3(0, 0, 0), new Vector3(0, 0, 10)], 2);
    const { left, right, dirs } = stripEdges(bend.pts, 4);
    for (const edge of [left, right]) {
      for (let i = 1; i < edge.length; i++) {
        expect(
          edge[i]!.clone()
            .sub(edge[i - 1]!)
            .dot(dirs[i]!)
        ).toBeGreaterThanOrEqual(0);
      }
    }
  });
});
