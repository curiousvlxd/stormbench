import { Vector3 } from 'three';
import { instance, type KitModel, type Placement } from '../assets/kit';
import { ROAD_W } from '../config';
import { distanceToPolygonEdge, pointInPolygon } from '../math/plane';
import type { Obstacles } from '../world/Obstacles';
import type { SceneContext } from './context';
import { buildingRadius, clearOfRoad, nearestStreet, Occupancy, type RoadCells, TREE, treeChance } from './placement';
import type { RoadRun } from './roads';
import { townCentre } from './Streets';

type BuildingKit = 'houses' | 'centre' | 'towers';

interface BuildingGroup {
  readonly kit: BuildingKit;
  readonly k: number;
  readonly variation: number;
  readonly placements: Placement[];
}

const STREET_REACH = 5;
const HOUSE_SIZE = 1.75;
const TOWN_HOUSE_SIZE = 2.5;
const TOWER_SHARE = 0.1;
const BUSH_SHARE = 0.25;
const GRID_STEP = 1.85;
const TREE_STEP = 1.7;

/** The bigger of a model's two ground dimensions. */
function footprint(model: KitModel): number {
  return Math.max(model.size.x, model.size.z);
}

/**
 * The houses and trees: houses along both sides of every street and in the blocks between, facing
 * the nearest road and never far from one, town houses and the odd tower near the centre; then trees and bushes, thick in
 * the park and along the banks, sparse in town. Everything keeps clear of roads, structures and water.
 */
export function buildTown(
  { scene, world, kits, rand }: SceneContext,
  obstacles: Obstacles,
  runs: readonly RoadRun[],
  roads: RoadCells
): void {
  const b = world.bounds;
  const centre = townCentre(world.places);
  const taken = new Occupancy();
  const groups = new Map<string, BuildingGroup>();
  const put = (kit: BuildingKit, k: number, variation: number, placement: Placement) => {
    const key = `${kit}:${k}:${variation}`;
    let group = groups.get(key);
    if (!group) {
      group = { kit, k, variation, placements: [] };
      groups.set(key, group);
    }
    group.placements.push(placement);
  };
  const { houses, centre: centreKit, towers, houseVariations: variations } = kits;

  const build = (q: Vector3) => {
    const c = world.catchmentAt(q);
    if (!c) return;
    const r = buildingRadius(q, centre);
    if (distanceToPolygonEdge(q, c.poly) < r * 0.8) return;
    const street = nearestStreet(q, runs);
    if (!clearOfRoad(street.distance, r) || street.distance > STREET_REACH || !roads.clear(q, r + 0.15)) return;
    if (obstacles.clearance(q) < r + 0.2 || !taken.free(q, r)) return;
    taken.take(q, r);
    const ry = street.facing;
    if (r > 1 && centreKit.length) {
      const tall = towers.length > 0 && rand() < TOWER_SHARE;
      const list = tall ? towers : centreKit;
      const k = Math.floor(rand() * list.length);
      const scale = TOWN_HOUSE_SIZE / footprint(list[k]!);
      put(tall ? 'towers' : 'centre', k, -1, { position: q, rotation: ry, scale });
    } else if (houses.length) {
      const k = Math.floor(rand() * houses.length);
      const scale = HOUSE_SIZE / footprint(houses[k]!);
      const v = Math.floor(rand() * (variations.length + 1)) - 1;
      put('houses', k, v, { position: q, rotation: ry, scale });
    }
  };

  for (const run of runs) {
    for (const side of [-1, 1]) {
      for (let d = run.from + 1.2; d < run.to - 1; d += 2.3 + rand() * 0.3) {
        if (rand() < 0.1) continue;
        const probe = run.horizontal ? new Vector3(d, 0, run.fixed) : new Vector3(run.fixed, 0, d);
        const off = side * (ROAD_W / 2 + 0.2 + buildingRadius(probe, centre));
        build(run.horizontal ? new Vector3(d, 0, run.fixed + off) : new Vector3(run.fixed + off, 0, d));
      }
    }
  }
  for (let x = b.minX - 2; x <= b.maxX + 2; x += GRID_STEP) {
    for (let z = b.minZ - 2; z <= b.maxZ + 2; z += GRID_STEP) {
      if (rand() < 0.3) continue;
      build(new Vector3(x + (rand() - 0.5) * 0.4, 0, z + (rand() - 0.5) * 0.4));
    }
  }
  for (const { kit, k, variation, placements } of groups.values()) {
    instance(scene, kits[kit][k]!, placements, { map: variation >= 0 ? (variations[variation] ?? null) : null });
  }

  const { trees, bushes } = kits;
  if (!trees.length) return;
  const woods = new Map<string, { proto: KitModel; placements: Placement[] }>();
  const plant = (list: readonly KitModel[], key: string, q: Vector3, height: number) => {
    const k = Math.floor(rand() * list.length);
    const proto = list[k]!;
    let wood = woods.get(key + k);
    if (!wood) {
      wood = { proto, placements: [] };
      woods.set(key + k, wood);
    }
    wood.placements.push({ position: q, rotation: rand() * Math.PI * 2, scale: height / proto.size.y });
  };
  const park = world.areas.find(a => /park/i.test(a.name));
  for (let x = b.minX - 45; x <= b.maxX + 45; x += TREE_STEP) {
    for (let z = b.minZ - 35; z <= b.maxZ + 35; z += TREE_STEP) {
      const q = new Vector3(x + (rand() - 0.5) * 1.2, 0, z + (rand() - 0.5) * 1.2);
      const inPark = !!park && pointInPolygon(q.x, q.z, park.poly);
      const urban = world.catchmentAt(q) !== null;
      const chance = treeChance(inPark, urban, world.nearestWater(q).distance);
      if (rand() > chance) continue;
      if (obstacles.clearance(q) < 0.6 || !taken.free(q, TREE)) continue;
      if (!roads.clear(q, 0.45)) continue;
      taken.take(q, TREE);
      if (bushes.length && rand() < BUSH_SHARE) plant(bushes, 'b', q, 0.5 + rand() * 0.4);
      else plant(trees, 't', q, 1.6 + rand() * 1.6);
    }
  }
  for (const { proto, placements } of woods.values()) instance(scene, proto, placements);
}
