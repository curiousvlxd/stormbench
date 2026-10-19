import {
  BufferGeometry,
  Color,
  DoubleSide,
  type FogExp2,
  Float32BufferAttribute,
  Mesh,
  type Scene,
  ShaderMaterial,
  Vector2,
  type Vector3
} from 'three';
import type { Polygon } from 'polygon-clipping';
import { flat } from '../assets/materials';
import { FOG_DENSITY, RIVER_SPOTS, RIVER_Y, SKY } from '../config';
import { riverFragment, riverVertex } from '../shaders/river';
import type { Picking } from '../ui/Picking';
import { BANK, bankHeight, channelHeight, densePath, insidePolygon, ringOf } from '../world/land';
import { stripEdges, type WaterPath } from '../world/water';
import { hash } from '../world/WaterSurfaces';

export interface RiverUniforms {
  readonly [name: string]: { value: unknown };
  readonly uTime: { value: number };
  readonly uAmp: { value: number };
  readonly uSpots: { value: Vector2[] };
  readonly uActive: { value: Vector2[] };
  readonly uDeep: { value: Color };
  readonly uSky: { value: Color };
  readonly uDirt: { value: Color };
  readonly uFog: { value: Color };
  readonly uMoonDir: { value: Vector3 };
  readonly uFogDensity: { value: number };
  readonly uLight: { value: number };
}

const FACET = 0.55;

/** Two triangles per quad of a strip `across` quads wide; `alternate` flips every other diagonal so facets do not line up. */
function quads(rows: number, across: number, alternate: boolean): number[] {
  const index: number[] = [];
  for (let i = 0; i < rows - 1; i++) {
    for (let k = 0; k < across; k++) {
      const p0 = i * (across + 1) + k;
      const p1 = p0 + 1;
      const q0 = p0 + across + 1;
      const q1 = q0 + 1;
      if (!alternate || (i + k) % 2) index.push(p0, q0, p1, p1, q0, q1);
      else index.push(p0, q0, q1, p0, q1, p1);
    }
  }
  return index;
}

/**
 * The faceted water of a river: rows of points across the stream about `FACET` apart, the inner ones
 * nudged by a fixed hash, every point with two phases of its own (`aPhase`) for the shader to bob it.
 * u runs along the river, v across it. Where it runs into a wider water, it sinks under that one.
 */
function waterGeometry(w: WaterPath, others: readonly WaterPath[]): BufferGeometry {
  const wider = others.filter(o => o.width > w.width).map(o => waterOutline(o));
  const positions: number[] = [];
  const uvs: number[] = [];
  const phases: number[] = [];
  const n = w.pts.length;
  const across = Math.max(4, Math.round(w.width / FACET));
  const { left, right, dirs, sides } = stripEdges(w.pts, w.width / 2);
  for (let i = 0; i < n; i++) {
    for (let k = 0; k <= across; k++) {
      const f = (k + (i % 2 && k > 0 && k < across ? 0.5 : 0)) / across;
      const edge = k === 0 || k === across;
      const q = left[i]!.clone().lerp(right[i]!, f);
      if (!edge && i > 0 && i < n - 1) {
        q.addScaledVector(sides[i]!, (hash(q.x, q.z, 0) - 0.5) * FACET * 0.4);
        q.addScaledVector(dirs[i]!, (hash(q.x, q.z, 1.7) - 0.5) * FACET * 0.4);
      }
      positions.push(q.x, wider.some(o => insidePolygon(q.x, q.z, o)) ? -0.2 : 0, q.z);
      uvs.push(w.cum[i]! / w.total, f);
      phases.push(hash(q.x, q.z, 3.1) * Math.PI * 2, hash(q.x, q.z, 5.3) * Math.PI * 2);
    }
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new Float32BufferAttribute(uvs, 2));
  geometry.setAttribute('aPhase', new Float32BufferAttribute(phases, 2));
  geometry.setIndex(quads(n, across, true));
  geometry.computeVertexNormals();
  return geometry;
}

/** The outline of a water's surface, out to its edges. */
function waterOutline(w: WaterPath): Polygon {
  const { left, right } = stripEdges(w.pts, w.width / 2);
  return [ringOf([...left, ...right.reverse()])];
}

/**
 * The channel a water runs in: each bank from its top on the ground down past the water's edge to
 * the bed, and the bed. Every point takes its height from all the waters, the lowest, so where two
 * meet their banks run into each other without a seam.
 */
function channelGeometry(w: WaterPath, waters: readonly WaterPath[]): BufferGeometry {
  const half = w.width / 2;
  const across = [half + BANK, half + 0.55, half + 0.3, half + 0.1, half - 0.1, half - 0.35, half - 0.9];
  const pts = densePath(w, waters);
  const edges = across.map(r => stripEdges(pts, r));
  const others = waters.filter(o => o !== w);
  const positions: number[] = [];
  for (let i = 0; i < pts.length; i++) {
    const row = [...edges.map(e => e.left[i]!), ...edges.map(e => e.right[i]!).reverse()];
    row.forEach((q, k) => {
      const own = bankHeight(across[k < across.length ? k : 2 * across.length - 1 - k]! - half);
      const y = others.length ? Math.min(own, channelHeight(q, others)) : own;
      positions.push(q.x, y, q.z);
    });
  }
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setIndex(quads(pts.length, across.length * 2 - 1, false));
  geometry.computeVertexNormals();
  return geometry;
}

/** The receiving waters: shaded low-poly streams in channels below the ground, brown where overflows spill into them. */
export class Rivers {
  private readonly surfaces = new Map<WaterPath, RiverUniforms>();

  constructor(scene: Scene, waters: readonly WaterPath[], sunDir: Vector3, picking: Picking) {
    for (const w of waters) {
      const geometry = waterGeometry(
        w,
        waters.filter(o => o !== w)
      );
      const uniforms: RiverUniforms = {
        uTime: { value: 0 },
        uAmp: { value: w.width > 3 ? 0.07 : 0.05 },
        uSpots: { value: Array.from({ length: RIVER_SPOTS }, () => new Vector2(0, 0)) },
        uActive: { value: Array.from({ length: RIVER_SPOTS }, () => new Vector2(0, 0)) },
        uDeep: { value: new Color(0x2a86c9) },
        uSky: { value: new Color(0x7cc6ee) },
        uDirt: { value: new Color(0x7d8c72) },
        uFog: { value: new Color(SKY.fog) },
        uMoonDir: { value: sunDir },
        uFogDensity: { value: FOG_DENSITY },
        uLight: { value: 1 }
      };
      const mesh = new Mesh(
        geometry,
        new ShaderMaterial({ uniforms, vertexShader: riverVertex, fragmentShader: riverFragment, side: DoubleSide })
      );
      mesh.position.y = RIVER_Y;
      scene.add(mesh);
      picking.add(mesh, 'river');
      this.surfaces.set(w, uniforms);
      const sand = new Mesh(channelGeometry(w, waters), flat(0xe8d49a, { roughness: 1, side: DoubleSide }));
      sand.receiveShadow = true;
      scene.add(sand);
    }
  }

  /** The shader inputs of a water, where the overflow channels mark their plumes. */
  uniformsOf(water: WaterPath): RiverUniforms {
    const uniforms = this.surfaces.get(water);
    if (!uniforms) throw new Error(`No river surface for ${water.name}.`);
    return uniforms;
  }

  /** Starts a frame at time t with clean water in `fog`, lit by `light` (1 by day); the channels then mark their plumes. */
  tick(t: number, fog: FogExp2, light = 1): void {
    for (const u of this.surfaces.values()) {
      u.uLight.value = light;
      for (const spot of u.uSpots.value) spot.set(0, 0);
      u.uTime.value = t;
      u.uFogDensity.value = fog.density;
      u.uFog.value.copy(fog.color);
    }
  }
}
