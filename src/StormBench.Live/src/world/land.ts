import polygonClipping, { type MultiPolygon, type Polygon, type Ring } from 'polygon-clipping';
import type { Vector3 } from 'three';
import { RIVER_Y } from '../config';
import { nearestWater, stripEdges, type WaterPath } from './water';

/** How far a water's banks reach past its edge, where they meet the ground. */
export const BANK = 0.8;

/** The bed of a channel, below the water. */
export const BED = RIVER_Y - 0.45;

/** Down a bank: from its top on the ground, past the water's edge, to the bed (distance from the water's edge, height). */
const PROFILE: readonly (readonly [number, number])[] = [
  [BANK, 0],
  [-0.2, RIVER_Y - 0.08],
  [-0.9, BED]
];

/** How high a channel's bank or bed is `edge` from its water's edge (negative inside the water). */
export function bankHeight(edge: number): number {
  if (edge >= PROFILE[0]![0]) return 0;
  for (let k = 1; k < PROFILE.length; k++) {
    const [e0, y0] = PROFILE[k - 1]!;
    const [e1, y1] = PROFILE[k]!;
    if (edge >= e1) return y0 + ((edge - e0) / (e1 - e0)) * (y1 - y0);
  }
  return BED;
}

/** The height of the ground in the channels at q: the lowest any water's banks reach there. */
export function channelHeight(q: Vector3, waters: readonly WaterPath[]): number {
  return Math.min(0, ...waters.map(w => bankHeight(nearestWater(q, [w]).distance)));
}

/** A closed ring of [x, z] points from points in the ground plane. */
export function ringOf(pts: readonly Vector3[]): Ring {
  const ring: Ring = pts.map(q => [q.x, q.z]);
  const first = ring[0];
  if (first) ring.push([first[0], first[1]]);
  return ring;
}

/** How many pieces a stretch of a water is cut into where another water is near, for the banks to follow it. */
const NEAR_STEPS = 5;

/**
 * The line of a water, with its stretches cut finer where another water's channel comes within
 * reach, so the banks of two waters that meet are drawn finely enough to run into each other.
 */
export function densePath(w: WaterPath, waters: readonly WaterPath[]): Vector3[] {
  const others = waters.filter(o => o !== w);
  const reach = w.width / 2 + BANK + 2;
  const pts: Vector3[] = [w.pts[0]!.clone()];
  for (let i = 1; i < w.pts.length; i++) {
    const a = w.pts[i - 1]!;
    const b = w.pts[i]!;
    const mid = a.clone().lerp(b, 0.5);
    const near = others.some(o => nearestWater(mid, [o]).distance < reach + BANK);
    const steps = near ? NEAR_STEPS : 1;
    for (let k = 1; k <= steps; k++) pts.push(a.clone().lerp(b, k / steps));
  }
  return pts;
}

/** The outline of a water's channel, out to the top of its banks, along `pts` (its line, or its dense line). */
export function channelOutline(w: WaterPath, pts: readonly Vector3[] = w.pts): Polygon {
  const { left, right } = stripEdges(pts, w.width / 2 + BANK);
  return [ringOf([...left, ...right.reverse()])];
}

/** What is left of a polygon once the waters' channels are cut out of it. */
export function withoutWaters(poly: Polygon, waters: readonly WaterPath[]): MultiPolygon {
  if (!waters.length) return [poly];
  return polygonClipping.difference(poly, ...waters.map(w => channelOutline(w, densePath(w, waters))));
}

/** Whether [x, z] lies inside a polygon (its outer ring and not in a hole). */
export function insidePolygon(x: number, z: number, poly: Polygon): boolean {
  const inRing = (ring: Ring) => {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
      const [xi, zi] = ring[i]!;
      const [xj, zj] = ring[j]!;
      if (zi > z !== zj > z && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) inside = !inside;
    }
    return inside;
  };
  const [outer, ...holes] = poly;
  return !!outer && inRing(outer) && !holes.some(inRing);
}
