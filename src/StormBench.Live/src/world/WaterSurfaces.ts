import { BufferGeometry, Float32BufferAttribute } from 'three';

/** A repeatable hash of a position, 0..1, so the facets are the same on every load. */
export function hash(x: number, z: number, k: number): number {
  const v = Math.sin(Math.round(x * 1000) * 12.9898 + Math.round(z * 1000) * 78.233 + k) * 43758.5453;
  return v - Math.floor(v);
}

/** Turns the triangles of a flat xz mesh to face up, whatever order they were wound in. */
function faceUp(geometry: BufferGeometry, index: number[]): BufferGeometry {
  const p = geometry.getAttribute('position');
  const [a, b, c] = [index[0]!, index[1]!, index[2]!];
  const ux = p.getX(b) - p.getX(a);
  const uz = p.getZ(b) - p.getZ(a);
  const vx = p.getX(c) - p.getX(a);
  const vz = p.getZ(c) - p.getZ(a);
  const up = uz * vx - ux * vz;
  if (up < 0) {
    for (let i = 0; i < index.length; i += 3) [index[i + 1], index[i + 2]] = [index[i + 2]!, index[i + 1]!];
  }
  geometry.setIndex(index);
  return geometry;
}

/** How a point of faceted water rises and falls: two slow sines at the vertex's own phases. */
export function facetHeight(t: number, phaseA: number, phaseB: number): number {
  return 0.65 * Math.sin(t * 1.6 + phaseA) + 0.35 * Math.sin(t * 2.7 + phaseB);
}

/**
 * Even triangles over a disc: ring i holds 6i points, so every facet is about the same size.
 * Points inside the rim are nudged a little so the pattern does not look machine-made.
 */
export function facetDisc(radius: number, rings: number): BufferGeometry {
  const positions: number[] = [0, 0, 0];
  const starts: number[] = [0];
  const counts: number[] = [1];
  const step = radius / rings;
  for (let i = 1; i <= rings; i++) {
    starts.push(positions.length / 3);
    const n = 6 * i;
    counts.push(n);
    const r = step * i;
    for (let k = 0; k < n; k++) {
      const a = (k / n) * Math.PI * 2 + (i % 2 ? Math.PI / n : 0);
      let x = Math.cos(a) * r;
      let z = Math.sin(a) * r;
      if (i < rings) {
        x += (hash(x, z, 0) - 0.5) * step * 0.45;
        z += (hash(x, z, 1.7) - 0.5) * step * 0.45;
      }
      positions.push(x, 0, z);
    }
  }
  const index: number[] = [];
  for (let i = 1; i <= rings; i++) {
    const inner = starts[i - 1]!;
    const outer = starts[i]!;
    const m = counts[i - 1]!;
    const n = counts[i]!;
    let a = 0;
    let b = 0;
    while (a < m || b < n) {
      const nextA = m === 1 ? Infinity : (a + 1) / m;
      const nextB = (b + 1) / n;
      const ia = inner + (a % m);
      const ib = outer + (b % n);
      if (nextB <= nextA) {
        index.push(ia, outer + ((b + 1) % n), ib);
        b++;
      } else {
        index.push(ia, inner + ((a + 1) % m), ib);
        a++;
      }
      if (m === 1 && b >= n) break;
    }
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  return faceUp(geometry, index);
}

/** Even triangles over a w x d rectangle: rows of points, every other row shifted by half a step. */
export function facetPlane(w: number, d: number, spacing: number): BufferGeometry {
  const cols = Math.max(2, Math.round(w / spacing));
  const rows = Math.max(2, Math.round(d / spacing));
  const positions: number[] = [];
  for (let j = 0; j <= rows; j++) {
    for (let i = 0; i <= cols; i++) {
      const shift = j % 2 && i > 0 && i < cols ? 0.5 : 0;
      let x = ((i + shift) / cols - 0.5) * w;
      let z = (j / rows - 0.5) * d;
      if (i > 0 && i < cols && j > 0 && j < rows) {
        x += (hash(x, z, 0) - 0.5) * (w / cols) * 0.35;
        z += (hash(x, z, 1.7) - 0.5) * (d / rows) * 0.35;
      }
      positions.push(Math.max(-w / 2, Math.min(w / 2, x)), 0, z);
    }
  }
  const index: number[] = [];
  for (let j = 0; j < rows; j++) {
    for (let i = 0; i < cols; i++) {
      const p0 = j * (cols + 1) + i;
      const p1 = p0 + 1;
      const q0 = p0 + cols + 1;
      const q1 = q0 + 1;
      if (j % 2) index.push(p0, q0, q1, p0, q1, p1);
      else index.push(p0, q0, p1, p1, q0, q1);
    }
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  return faceUp(geometry, index);
}

/**
 * The low-poly water in tanks and basins: even facets, every point bobbing at its own phase, so
 * the faces tilt against each other and catch the sun one by one.
 */
export class WaterSurfaces {
  private readonly surfaces: { geometry: BufferGeometry; phases: Float32Array; amplitude: number }[] = [];

  /** A disc of water, flat in xz. */
  disc(radius: number, amplitude = 0.05): BufferGeometry {
    return this.track(facetDisc(radius, Math.max(3, Math.round(radius / 0.32))), amplitude);
  }

  /** A w x d rectangle of water, flat in xz. */
  plane(w: number, d: number, amplitude = 0.04): BufferGeometry {
    return this.track(facetPlane(w, d, 0.34), amplitude);
  }

  /** Lets every surface bob at time t. */
  tick(t: number): void {
    for (const { geometry, phases, amplitude } of this.surfaces) {
      const position = geometry.getAttribute('position');
      const a = position.array;
      for (let i = 0, v = 0; i < a.length; i += 3, v += 2) {
        a[i + 1] = facetHeight(t, phases[v]!, phases[v + 1]!) * amplitude;
      }
      position.needsUpdate = true;
    }
  }

  /** Gives every point its two phases and remembers the surface for `tick`. */
  private track(geometry: BufferGeometry, amplitude: number): BufferGeometry {
    const position = geometry.getAttribute('position');
    const phases = new Float32Array(position.count * 2);
    for (let i = 0; i < position.count; i++) {
      const x = position.getX(i);
      const z = position.getZ(i);
      phases[i * 2] = hash(x, z, 3.1) * Math.PI * 2;
      phases[i * 2 + 1] = hash(x, z, 5.3) * Math.PI * 2;
    }
    this.surfaces.push({ geometry, phases, amplitude });
    geometry.computeVertexNormals();
    return geometry;
  }
}
