import { BoxGeometry, InstancedMesh, type Material, Matrix4, Vector3 } from 'three';
import { instance, type Placement, type RoadTile } from '../assets/kit';
import { flat } from '../assets/materials';
import { place, roleOf } from '../assets/structures';
import { BANK } from '../world/land';
import { BLOCK_X, BLOCK_Z, ROAD_W, TILE } from '../config';
import { distanceToPolygonEdge, distanceToSegment, pointInPolygon } from '../math/plane';
import type { Obstacles } from '../world/Obstacles';
import type { SceneContext } from './context';
import { RoadCells } from './placement';
import { LAMP_OFFSET, StreetLights } from './StreetLights';
import {
  type Cell,
  chooseTile,
  cutSpurs,
  type GridFrame,
  type Ground,
  joinRoads,
  layRoads,
  OPEN,
  openSquares,
  pruneRoads,
  type RoadRun,
  roadPieces,
  roadRuns,
  routeRoad,
  type RouteRules,
  tileTurn
} from './roads';

const MARGIN = 40;
const CROSSING_REACH = 16;
const ROAD_Y = 0.016;
const TURN = 8;
const BRIDGE = 1.5;
const BESIDE = 4;
const NEAR = 1.5;
const BY_WATER = 2;
const SPUR = 2;

/** The town centre, if the map names one. */
export function townCentre(places: readonly { name: string; at: Vector3 }[]): Vector3 | undefined {
  return places.find(pl => /town centre/i.test(pl.name))?.at;
}

/**
 * The streets: a block grid laid over the subcatchments wherever it keeps clear of structures
 * and water, tidied, then joined into one network by roads between the districts, over bridges
 * where they cross a water, with two roads out of town. Drawn with the Kenney road tiles that fit
 * each cell, with lamps; the bridges are our model, a span per cell and a pier in the stream.
 */
export class Streets {
  readonly runs: readonly RoadRun[];
  readonly roads: RoadCells;
  readonly straights: readonly { readonly at: Vector3; readonly alongX: boolean }[];
  readonly lights: StreetLights | null = null;

  constructor({ scene, world, kits, structures }: SceneContext, obstacles: Obstacles) {
    const b = world.bounds;
    const x0 = b.minX - MARGIN;
    const z0 = b.minZ - MARGIN;
    const frame: GridFrame = {
      x0,
      z0,
      tile: TILE,
      nx: Math.ceil((b.maxX + MARGIN - x0) / TILE),
      nz: Math.ceil((b.maxZ + MARGIN - z0) / TILE),
      every: { x: Math.round(BLOCK_X / TILE), z: Math.round(BLOCK_Z / TILE) }
    };
    const centreOf = (i: number, j: number) => new Vector3(x0 + i * TILE, 0, z0 + j * TILE);
    const blocked = (q: Vector3) =>
      obstacles.circles.some(([p, r]) => p.distanceTo(q) < r + ROAD_W / 2 + 0.3) ||
      world.waters.some(w =>
        w.pts.some((pt, i) => i > 0 && distanceToSegment(q, w.pts[i - 1]!, pt) < w.width / 2 + ROAD_W / 2 + 0.8)
      );
    const usable = (q: Vector3) => {
      const c = world.catchmentAt(q);
      return !!c && distanceToPolygonEdge(q, c.poly) > 0.8 && !blocked(q);
    };
    const grid = layRoads(frame, (i, j) => usable(centreOf(i, j)));
    pruneRoads(grid);
    for (const c of world.catchments) this.mainStreet(grid, frame, c.poly, centreOf, usable);
    const widest = Math.max(0, ...world.waters.map(w => w.width));
    const overWater = BANK + TILE * 0.5;
    const ground = (i: number, j: number): Ground => {
      const q = centreOf(i, j);
      if (obstacles.circles.some(([p, r]) => p.distanceTo(q) < r + ROAD_W / 2)) return 'blocked';
      return world.nearestWater(q).distance < overWater ? 'water' : 'land';
    };
    const known = new Map<number, Ground>();
    const rules: RouteRules = {
      ground: (i, j) => {
        const key = i * 100000 + j;
        let g = known.get(key);
        if (!g) {
          g = ground(i, j);
          known.set(key, g);
        }
        return g;
      },
      turn: TURN,
      bridge: BRIDGE,
      longestBridge: Math.ceil((widest + 2 * overWater) / TILE) + 1,
      beside: BESIDE,
      near: NEAR,
      extra: (i, j) => {
        const d = world.nearestWater(centreOf(i, j)).distance;
        return d >= overWater && d < overWater + TILE * 1.5 ? BY_WATER : 0;
      },
      bank: (i, j) => {
        const q = centreOf(i, j);
        const hit = world.nearestWater(q);
        const across = q.clone().sub(hit.centre).dot(hit.normal);
        return `${hit.water.name}:${across > 0 ? 1 : -1}`;
      }
    };
    const art = (label: string) => {
      const rows: string[] = [];
      for (let j = 0; j < frame.nz; j++) {
        let row = '';
        for (let i = 0; i < frame.nx; i++)
          row += grid.has(i, j)
            ? '#'
            : rules.ground(i, j) === 'water'
              ? '~'
              : rules.ground(i, j) === 'blocked'
                ? 'x'
                : '.';
        rows.push(row);
      }
      (window as unknown as Record<string, string>)['roads_' + label] = rows.join(String.fromCharCode(10));
    };
    art('laid');
    const bridges = joinRoads(grid, frame, rules);
    art('joined');
    const main = roadPieces(grid)[0] ?? [];
    const exits = new Set<string>();
    for (const edge of [(i: number) => i === 0, (i: number) => i === frame.nx - 1]) {
      const exit = main.length ? routeRoad(frame, main, i => edge(i), rules, grid) : null;
      for (const [i, j] of exit ?? []) {
        grid.add(i, j);
        exits.add(`${i},${j}`);
        if (rules.ground(i, j) === 'water') bridges.push([i, j]);
      }
    }
    art('exits');
    openSquares(grid);
    art('squares');
    cutSpurs(grid, SPUR, exits);
    art('spurs');
    const spans = bridges.filter(([i, j]) => grid.has(i, j));
    this.runs = roadRuns(grid, frame);
    const onBridge = new Set(spans.map(([i, j]) => `${i},${j}`));
    this.roads = new RoadCells(x0, z0, TILE, grid.list());
    this.straights = grid
      .list()
      .filter(([i, j]) => !onBridge.has(`${i},${j}`))
      .flatMap(([i, j]): { at: Vector3; alongX: boolean }[] => {
        const [e, s, w, n] = grid.links(i, j);
        if (e && w && !s && !n) return [{ at: centreOf(i, j), alongX: true }];
        if (s && n && !e && !w) return [{ at: centreOf(i, j), alongX: false }];
        return [];
      });
    this.buildBridges(scene, structures, spans, grid, centreOf, world);

    const centre = townCentre(world.places);
    const tiles = new Map<RoadTile, Placement[]>();
    const lamps: Placement[] = [];
    const streets = new Set<Material>();
    for (const [i, j] of grid.list()) {
      if (onBridge.has(`${i},${j}`)) continue;
      const want = grid.links(i, j);
      const q = centreOf(i, j);
      const crossing = !!centre && q.distanceTo(centre) < CROSSING_REACH && grid.besideJunction(i, j);
      const name = chooseTile(want, crossing);
      let list = tiles.get(name);
      if (!list) {
        list = [];
        tiles.set(name, list);
      }
      list.push({ position: q.clone().setY(ROAD_Y), rotation: tileTurn(OPEN[name], want), scale: TILE });
      if (name === 'road-straight' && (i + j) % 3 === 0) {
        lamps.push(
          want[0]
            ? { position: q.clone().add(new Vector3(0, ROAD_Y, TILE * LAMP_OFFSET)), rotation: 0, scale: TILE }
            : {
                position: q.clone().add(new Vector3(-TILE * LAMP_OFFSET, ROAD_Y, 0)),
                rotation: -Math.PI / 2,
                scale: TILE
              }
        );
      }
    }
    for (const [name, placements] of tiles) {
      const model = kits.roads[name];
      if (model) {
        for (const mesh of instance(scene, model, placements, { shadow: false }))
          streets.add(mesh.material as Material);
        continue;
      }
      const mesh = new InstancedMesh(
        new BoxGeometry(TILE, 0.03, TILE),
        flat(0x4b5059, { roughness: 0.9 }),
        placements.length
      );
      const m = new Matrix4();
      placements.forEach((pl, k) => mesh.setMatrixAt(k, m.makeTranslation(pl.position.x, 0.03, pl.position.z)));
      mesh.receiveShadow = true;
      scene.add(mesh);
    }
    const lamp = kits.lamps[0];
    if (lamp) {
      instance(scene, lamp, lamps);
      this.lights = new StreetLights(scene, lamp, lamps, [...streets]);
    }
  }

  /**
   * Gives a subcatchment that the block grid left without a street one: the longest straight run
   * of usable cells along a row or a column inside it, if it is three cells or more.
   */
  private mainStreet(
    grid: ReturnType<typeof layRoads>,
    frame: GridFrame,
    poly: readonly Vector3[],
    centreOf: (i: number, j: number) => Vector3,
    usable: (q: Vector3) => boolean
  ): void {
    const inside = (i: number, j: number) => {
      const q = centreOf(i, j);
      return pointInPolygon(q.x, q.z, poly);
    };
    let best: Cell[] = [];
    const open = new Set<string>();
    for (let i = 0; i < frame.nx; i++) {
      for (let j = 0; j < frame.nz; j++) {
        if (!inside(i, j)) continue;
        if (grid.has(i, j)) return;
        if (usable(centreOf(i, j))) open.add(`${i},${j}`);
      }
    }
    for (const [di, dj] of [
      [1, 0],
      [0, 1]
    ] as const) {
      for (const key of open) {
        const [i, j] = key.split(',').map(Number) as [number, number];
        if (open.has(`${i - di},${j - dj}`)) continue;
        const run: Cell[] = [];
        for (let k = 0; open.has(`${i + k * di},${j + k * dj}`); k++) run.push([i + k * di, j + k * dj]);
        if (run.length > best.length) best = run;
      }
    }
    if (best.length >= 3) for (const [i, j] of best) grid.add(i, j);
  }

  /** A bridge span on every cell over water, turned along the road, and a pier under every other span that stands in the stream. */
  private buildBridges(
    scene: SceneContext['scene'],
    structures: SceneContext['structures'],
    cells: readonly Cell[],
    grid: ReturnType<typeof layRoads>,
    centreOf: (i: number, j: number) => Vector3,
    world: SceneContext['world']
  ): void {
    cells.forEach(([i, j], k) => {
      const q = centreOf(i, j).setY(ROAD_Y + 0.02);
      const [east] = grid.links(i, j);
      const turn = east ? Math.PI / 2 : 0;
      const span = place(roleOf(structures.bridge, 'deck'));
      span.position.copy(q);
      span.rotation.y = turn;
      scene.add(span);
      const hit = world.nearestWater(q);
      if (k % 2 === 0 && hit.distance < -0.3) {
        const pier = place(roleOf(structures.bridge, 'pier'));
        pier.position.copy(q);
        pier.rotation.y = turn;
        scene.add(pier);
      }
    });
  }
}
