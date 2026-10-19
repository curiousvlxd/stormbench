export interface Flat {
  readonly x: number;
  readonly z: number;
}

/** Whether (x, z) lies inside the polygon (even-odd rule). */
export function pointInPolygon(x: number, z: number, poly: readonly Flat[]): boolean {
  let inside = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const a = poly[i]!;
    const b = poly[j]!;
    if (a.z > z !== b.z > z && x < ((b.x - a.x) * (z - a.z)) / (b.z - a.z) + a.x) inside = !inside;
  }
  return inside;
}

/** Distance in the ground plane from p to the segment ab. */
export function distanceToSegment(p: Flat, a: Flat, b: Flat): number {
  const abx = b.x - a.x;
  const abz = b.z - a.z;
  const len2 = abx * abx + abz * abz || 1e-9;
  const t = Math.max(0, Math.min(1, ((p.x - a.x) * abx + (p.z - a.z) * abz) / len2));
  return Math.hypot(a.x + abx * t - p.x, a.z + abz * t - p.z);
}

/** Distance in the ground plane from q to the nearest edge of the polygon. */
export function distanceToPolygonEdge(q: Flat, poly: readonly Flat[]): number {
  let d = Infinity;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++)
    d = Math.min(d, distanceToSegment(q, poly[j]!, poly[i]!));
  return d;
}

/** The smallest turn between two angles, in radians (0 to pi). */
export function angleGap(a: number, b: number): number {
  return Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
}
