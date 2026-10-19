import { CatmullRomCurve3, Vector3 } from 'three';

export interface WaterPath {
  readonly name: string;
  readonly pts: readonly Vector3[];
  readonly cum: readonly number[];
  readonly total: number;
  readonly width: number;
}

export interface WaterHit {
  readonly point: Vector3;
  readonly centre: Vector3;
  readonly normal: Vector3;
  readonly water: WaterPath;
  readonly u: number;
  readonly distance: number;
}

/** How far a water runs on past the end of its line when nothing receives it there: beyond the fog. */
export const RUN_ON = 160;

/**
 * Waters run on past the ends of their lines, straight along the last stretch and in steps as long
 * as the line's own, so they do not stop in the open; an end that meets another water stays where
 * it is.
 */
export function runOn(lines: readonly (readonly Vector3[])[], meets = 3): Vector3[][] {
  const near = (q: Vector3, own: number) =>
    lines.some((other, k) => k !== own && other.some((o, i) => i > 0 && segmentDistance(q, other[i - 1]!, o) < meets));
  return lines.map((line, k) => {
    const out = [...line];
    if (line.length < 2) return out;
    const first = line[0]!;
    const last = line[line.length - 1]!;
    if (!near(first, k))
      out.unshift(first.clone().addScaledVector(first.clone().sub(line[1]!).setY(0).normalize(), RUN_ON));
    if (!near(last, k))
      out.push(
        last.clone().addScaledVector(
          last
            .clone()
            .sub(line[line.length - 2]!)
            .setY(0)
            .normalize(),
          RUN_ON
        )
      );
    return out;
  });
}

function segmentDistance(q: Vector3, a: Vector3, b: Vector3): number {
  const ab = b.clone().sub(a).setY(0);
  const t = Math.max(0, Math.min(1, q.clone().sub(a).setY(0).dot(ab) / Math.max(1e-9, ab.lengthSq())));
  return a.clone().addScaledVector(ab, t).setY(0).distanceTo(q.clone().setY(0));
}

/** A smooth path along a water's line: evenly spaced samples, their arc length, the width. */
export function waterPath(name: string, line: readonly Vector3[], width: number): WaterPath {
  const curve = new CatmullRomCurve3([...line], false, 'centripetal');
  curve.arcLengthDivisions = Math.max(200, Math.round(curve.getLength() * 8));
  curve.updateArcLengths();
  const n = Math.max(40, Math.round(curve.getLength() * 2));
  const pts = curve.getSpacedPoints(n);
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1]! + pts[i]!.distanceTo(pts[i - 1]!));
  return { name, pts, cum, total: cum[cum.length - 1]!, width };
}

/**
 * The nearest point on any water's bank: the bank point, the centre line point, the line's normal,
 * the water, where along it (0..1) and how far q is from the bank (negative inside the water).
 */
export function nearestWater(q: Vector3, waters: readonly WaterPath[]): WaterHit {
  let best: { d: number; centre: Vector3; water: WaterPath; ab: Vector3; u: number } | null = null;
  for (const w of waters) {
    for (let i = 1; i < w.pts.length; i++) {
      const a = w.pts[i - 1]!;
      const b = w.pts[i]!;
      const ab = b.clone().sub(a);
      const t = Math.max(0, Math.min(1, q.clone().sub(a).dot(ab) / ab.lengthSq()));
      const c = a.clone().addScaledVector(ab, t);
      const d = c.distanceTo(q.clone().setY(0));
      if (!best || d < best.d) {
        best = { d, centre: c, water: w, ab, u: (w.cum[i - 1]! + t * (w.cum[i]! - w.cum[i - 1]!)) / w.total };
      }
    }
  }
  if (!best) throw new Error('The town has no water.');
  const towards = q.clone().setY(0).sub(best.centre).setY(0);
  const bank = best.centre
    .clone()
    .addScaledVector(towards.lengthSq() > 1e-6 ? towards.normalize() : new Vector3(0, 0, -1), best.water.width / 2);
  const normal = new Vector3(-best.ab.z, 0, best.ab.x).normalize();
  return {
    point: bank,
    centre: best.centre,
    normal,
    water: best.water,
    u: best.u,
    distance: best.d - best.water.width / 2
  };
}

export interface StripEdges {
  readonly left: Vector3[];
  readonly right: Vector3[];
  readonly dirs: Vector3[];
  readonly sides: Vector3[];
}

/**
 * Both edges of a strip `half` either side of a line. On the inside of a bend tighter than `half`
 * the edge would run backwards and fold the strip over; there it waits instead.
 */
export function stripEdges(pts: readonly Vector3[], half: number): StripEdges {
  const n = pts.length;
  const left: Vector3[] = [];
  const right: Vector3[] = [];
  const dirs: Vector3[] = [];
  const sides: Vector3[] = [];
  for (let i = 0; i < n; i++) {
    const a = pts[Math.max(0, i - 1)]!;
    const b = pts[Math.min(n - 1, i + 1)]!;
    const dir = b.clone().sub(a).setY(0).normalize();
    const side = new Vector3(-dir.z, 0, dir.x);
    dirs.push(dir);
    sides.push(side);
    for (const [list, sign] of [
      [left, -1],
      [right, 1]
    ] as const) {
      const q = pts[i]!.clone().addScaledVector(side, sign * half);
      const prev = list[i - 1];
      list.push(prev && q.clone().sub(prev).dot(dir) <= 0.02 ? prev.clone() : q);
    }
  }
  return { left, right, dirs, sides };
}
