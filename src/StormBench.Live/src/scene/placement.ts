import { ROAD_W } from '../config';
import type { Flat } from '../math/plane';
import type { RoadRun } from './roads';

export interface Street {
  readonly distance: number;
  readonly facing: number;
}

export const CENTRE_REACH = 15;
export const TOWN_HOUSE = 1.25;
export const HOUSE = 0.92;
export const TREE = 0.45;

/** The nearest stretch of road to q: how far it is, and the turn (about y) that faces a house towards it. */
export function nearestStreet(q: Flat, runs: readonly RoadRun[]): Street {
  let distance = Infinity;
  let facing = 0;
  for (const r of runs) {
    const along = r.horizontal ? q.x : q.z;
    const across = r.horizontal ? q.z - r.fixed : q.x - r.fixed;
    const off = Math.max(0, r.from - along, along - r.to);
    const d = Math.hypot(off, across);
    if (d < distance) {
      distance = d;
      facing = r.horizontal ? (across < 0 ? 0 : Math.PI) : across < 0 ? Math.PI / 2 : -Math.PI / 2;
    }
  }
  return { distance, facing };
}

/** The footprint radius of a building at q: town houses near the centre, smaller houses elsewhere. */
export function buildingRadius(q: Flat, centre: Flat | undefined): number {
  if (!centre) return HOUSE;
  const dx = q.x - centre.x;
  const dz = q.z - centre.z;
  return Math.sqrt(dx * dx + dz * dz) < CENTRE_REACH ? TOWN_HOUSE : HOUSE;
}

/** Whether a building of radius r at q leaves the road clear, with a pavement. */
export function clearOfRoad(streetDistance: number, r: number): boolean {
  return streetDistance >= ROAD_W / 2 + 0.15 + r;
}

/** How likely a tree is at a spot: thick in the park, sparse in town, lining the banks out in the fields. */
export function treeChance(inPark: boolean, urban: boolean, waterDistance: number): number {
  if (!urban && waterDistance < 4 && waterDistance > 0.6) return 0.3;
  return inPark ? 0.32 : urban ? 0.05 : 0.07;
}

/** The ground already taken by buildings and trees, as circles. */
export class Occupancy {
  private readonly taken: { q: Flat; r: number }[] = [];

  /** Whether a circle of radius r at q overlaps nothing taken. */
  free(q: Flat, r: number): boolean {
    return !this.taken.some(({ q: p, r: pr }) => Math.hypot(p.x - q.x, p.z - q.z) < r + pr);
  }

  take(q: Flat, r: number): void {
    this.taken.push({ q, r });
  }
}

/** The cells of the road network, for what must keep off them: houses and trees. */
export class RoadCells {
  private readonly cells = new Set<string>();

  constructor(
    private readonly x0: number,
    private readonly z0: number,
    private readonly tile: number,
    cells: readonly (readonly [number, number])[]
  ) {
    for (const [i, j] of cells) this.cells.add(`${i},${j}`);
  }

  /** Whether a disc of radius r at q stays off every road cell. */
  clear(q: Flat, r: number): boolean {
    const { x0, z0, tile } = this;
    const reach = Math.ceil(r / tile) + 1;
    const ci = Math.round((q.x - x0) / tile);
    const cj = Math.round((q.z - z0) / tile);
    for (let i = ci - reach; i <= ci + reach; i++) {
      for (let j = cj - reach; j <= cj + reach; j++) {
        if (!this.cells.has(`${i},${j}`)) continue;
        if (Math.abs(q.x - (x0 + i * tile)) < tile / 2 + r && Math.abs(q.z - (z0 + j * tile)) < tile / 2 + r)
          return false;
      }
    }
    return true;
  }
}
