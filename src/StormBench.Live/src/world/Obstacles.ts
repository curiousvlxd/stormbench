import { Vector3 } from 'three';
import { TANK_R } from '../config';
import { distanceToSegment } from '../math/plane';
import type { LinkDescription } from '../types';
import type { WaterPath } from './water';

export type Circle = readonly [centre: Vector3, radius: number];
export type Segment = readonly [a: Vector3, b: Vector3, halfWidth: number];

/** Things houses, trees and streets keep clear of: circles, segments and the waters with their banks. */
export class Obstacles {
  constructor(
    readonly circles: readonly Circle[],
    readonly segments: readonly Segment[],
    private readonly waters: readonly WaterPath[]
  ) {}

  /** How far q is from the nearest obstacle's edge (negative inside one). */
  clearance(q: Vector3): number {
    let d = Infinity;
    for (const [p, r] of this.circles) d = Math.min(d, p.distanceTo(q) - r);
    for (const [a, b, w] of this.segments) d = Math.min(d, distanceToSegment(q, a, b) - w);
    for (const w of this.waters) {
      for (let i = 1; i < w.pts.length; i++) {
        d = Math.min(d, distanceToSegment(q, w.pts[i - 1]!, w.pts[i]!) - w.width / 2 - 1.2);
      }
    }
    return d;
  }
}

/**
 * A spot near `near` that keeps `clearance` from every node and pipe of the network: `near` itself
 * if it is free, else the first free one on rings around it.
 */
export function freeSpot(
  near: Vector3,
  clearance: number,
  pos: ReadonlyMap<string, Vector3>,
  links: readonly LinkDescription[]
): Vector3 {
  const occupied = [...pos.values()];
  const ok = (q: Vector3) =>
    occupied.every(p => p.distanceTo(q) > clearance + TANK_R) &&
    links.every(l => {
      const a = pos.get(l.from);
      const b = pos.get(l.to);
      return !a || !b || distanceToSegment(q, a, b) > clearance * 0.6;
    });
  if (ok(near)) return near;
  for (let r = 2; r < 40; r += 1.5) {
    for (let k = 0; k < 16; k++) {
      const a = (k / 16) * Math.PI * 2;
      const q = near.clone().add(new Vector3(Math.cos(a) * r, 0, Math.sin(a) * r));
      if (ok(q)) return q;
    }
  }
  return near;
}
