import { Vector3 } from 'three';
import { DEFAULT_WATER_WIDTH, SCALE, TANK_REACH, WATER_WIDTH } from '../config';
import { type Flat, pointInPolygon } from '../math/plane';
import type { MapPoint, NetworkDescription } from '../types';
import {
  clearChambersOffTanks,
  clearPlantOffWater,
  clearStructuresOffWater,
  moveJunctionsOffWater,
  spreadAlongBank
} from './nudge';
import { nearestWater, runOn, type WaterHit, type WaterPath, waterPath } from './water';

export interface Bounds {
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
}

export interface Catchment {
  readonly id: string;
  readonly poly: readonly Vector3[];
  readonly tint: string;
}

export interface Named {
  readonly name: string;
}

export interface Area extends Named {
  readonly poly: readonly Vector3[];
}

export interface Place extends Named {
  readonly at: Vector3;
}

export interface Gauge {
  readonly id: string;
  readonly at: Vector3;
}

/** The middle of a bounds rectangle, on the ground. */
export function middle(b: Bounds): Vector3 {
  return new Vector3((b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2);
}

/**
 * The town in scene coordinates: where every node stands once the structures are moved off the
 * water, the subcatchments, areas, places, rain gauges and receiving waters, and the bounds.
 */
export class World {
  readonly pos: Map<string, Vector3>;
  readonly bounds: Bounds;
  readonly tankSet: ReadonlySet<string>;
  readonly catchments: readonly Catchment[];
  readonly gauges: readonly Gauge[];
  readonly areas: readonly Area[];
  readonly places: Place[];
  readonly waters: readonly WaterPath[];

  constructor(readonly network: NetworkDescription) {
    const net = network;
    const xs = net.nodes.map(n => n.x);
    const ys = net.nodes.map(n => n.y);
    const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
    const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
    const toScene = ([x, y]: MapPoint) => new Vector3((x - cx) * SCALE, 0, -(y - cy) * SCALE);
    this.pos = new Map(net.nodes.map(n => [n.id, toScene([n.x, n.y])]));
    const ps = [...this.pos.values()];
    this.bounds = {
      minX: Math.min(...ps.map(p => p.x)),
      maxX: Math.max(...ps.map(p => p.x)),
      minZ: Math.min(...ps.map(p => p.z)),
      maxZ: Math.max(...ps.map(p => p.z))
    };
    this.tankSet = new Set(net.tanks);
    const chambers = net.overflowPoints.filter(id => !this.tankSet.has(id));
    clearChambersOffTanks(this.pos, net.tanks, chambers);

    const map = net.map ?? {};
    this.catchments = (map.catchments ?? [])
      .filter(c => (c.polygon?.length ?? 0) >= 3)
      .map(c => ({ id: c.id, poly: (c.polygon ?? []).map(toScene), tint: map.tints?.[c.id] ?? '#e6eef2' }));
    this.gauges = (map.gauges ?? []).map(g => ({ id: g.id, at: toScene([g.x, g.y]) }));
    this.areas = (map.areas ?? []).map(a => ({ name: a.name, poly: a.polygon.map(toScene) }));
    this.places = (map.places ?? []).map(pl => ({ name: pl.name, at: toScene([pl.x, pl.y]) }));
    const lines = (map.waters ?? [])
      .filter(w => (w.line?.length ?? 0) >= 2)
      .map(w => ({ name: w.name, line: (w.line ?? []).map(toScene) }));
    if (!lines.length) {
      const b = this.bounds;
      const z = b.maxZ + 8;
      lines.push({ name: 'River', line: [new Vector3(b.minX - 60, 0, z), new Vector3(b.maxX + 60, 0, z)] });
      this.places.push({ name: 'River', at: new Vector3((b.minX + b.maxX) / 2, 0, z) });
    }
    const run = runOn(lines.map(w => w.line));
    this.waters = lines.map((w, k) => waterPath(w.name, run[k]!, WATER_WIDTH[w.name] ?? DEFAULT_WATER_WIDTH));

    clearStructuresOffWater(
      this.pos,
      [...net.tanks, ...chambers],
      id => (this.tankSet.has(id) ? TANK_REACH : 2.0),
      this.waters
    );
    const structures = new Set([...net.tanks, ...net.overflowPoints, net.wwtp]);
    spreadAlongBank(this.pos, moveJunctionsOffWater(this.pos, net.links, structures, this.waters), this.waters);
    const outfall = this.pos.get(net.wwtp);
    if (outfall) clearPlantOffWater(outfall, this.waters);

    for (const c of this.catchments) for (const q of c.poly) this.extend(q);
    for (const w of lines) for (const q of w.line) if (Math.abs(q.x) < 90) this.extend(q);
  }

  /** Grows the bounds to take in q. */
  extend(q: Flat): void {
    const b = this.bounds;
    b.minX = Math.min(b.minX, q.x);
    b.maxX = Math.max(b.maxX, q.x);
    b.minZ = Math.min(b.minZ, q.z);
    b.maxZ = Math.max(b.maxZ, q.z);
  }

  /** The nearest point on the bank of any water, or of `only`. */
  nearestWater(q: Vector3, only: WaterPath | null = null): WaterHit {
    return nearestWater(q, only ? [only] : this.waters);
  }

  /** The subcatchment q lies in, or null. */
  catchmentAt(q: Flat): Catchment | null {
    return this.catchments.find(c => pointInPolygon(q.x, q.z, c.poly)) ?? null;
  }
}
