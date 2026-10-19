import { BoxGeometry, type BufferGeometry, CylinderGeometry, Float32BufferAttribute, Quaternion, Vector3 } from 'three';
import { RoundedBoxGeometry } from 'three/examples/jsm/geometries/RoundedBoxGeometry.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';

const KEEP = new Set(['position', 'normal', 'uv']);

/** Merges parts into one geometry with position, normal and uv only. */
export function merge(parts: readonly BufferGeometry[]): BufferGeometry {
  const prepared = parts.map(part => {
    const g = part.index ? part.toNonIndexed() : part.clone();
    for (const name of Object.keys(g.attributes)) {
      if (!KEEP.has(name)) g.deleteAttribute(name);
    }
    if (!g.hasAttribute('normal')) g.computeVertexNormals();
    if (!g.hasAttribute('uv')) {
      g.setAttribute('uv', new Float32BufferAttribute(new Float32Array(g.getAttribute('position').count * 2), 2));
    }
    g.clearGroups();
    return g;
  });
  return mergeGeometries(prepared, false);
}

/** A box w x h x d centred at (x, y, z), its edges rounded by `radius` when given. */
export function box(w: number, h: number, d: number, x = 0, y = 0, z = 0, radius = 0): BufferGeometry {
  const g = radius > 0 ? new RoundedBoxGeometry(w, h, d, 2, radius) : new BoxGeometry(w, h, d);
  g.translate(x, y, z);
  return g;
}

/** An upright cylinder centred at (x, y, z). */
export function cylinder(rTop: number, rBottom: number, h: number, x = 0, y = 0, z = 0, segments = 16): BufferGeometry {
  const g = new CylinderGeometry(rTop, rBottom, h, segments);
  g.translate(x, y, z);
  return g;
}

/** A cylinder from a to b. */
export function rod(a: Vector3, b: Vector3, radius: number, segments = 8): BufferGeometry {
  const dir = new Vector3().subVectors(b, a);
  const g = new CylinderGeometry(radius, radius, dir.length(), segments);
  const q = new Quaternion().setFromUnitVectors(new Vector3(0, 1, 0), dir.clone().normalize());
  g.applyQuaternion(q);
  const mid = a.clone().addScaledVector(dir, 0.5);
  g.translate(mid.x, mid.y, mid.z);
  return g;
}
