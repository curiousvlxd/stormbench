import { Vector3 } from 'three';
import { PLANT_MIDDLE, PLANT_RADIUS, PLANT_SITE, TANK_R } from '../config';
import type { LinkDescription } from '../types';
import { nearestWater, type WaterPath } from './water';

export type Positions = Map<string, Vector3>;

/** The direction of `v` in the ground plane, or `fallback` when v has no length. */
function directionOr(v: Vector3, fallback: Vector3): Vector3 {
  return v.lengthSq() > 1e-6 ? v.normalize() : fallback;
}

/** Overflow chambers that stand inside a tank's footprint move out to a clear ring around it. */
export function clearChambersOffTanks(pos: Positions, tanks: readonly string[], chambers: readonly string[]): void {
  const clear = TANK_R + 3.6;
  for (const id of chambers) {
    const p = pos.get(id);
    if (!p) continue;
    for (const tank of tanks) {
      const c = pos.get(tank);
      if (!c) continue;
      const away = p.clone().sub(c).setY(0);
      if (away.length() < clear) p.copy(c).addScaledVector(directionOr(away, new Vector3(1, 0, 0)), clear);
    }
  }
}

/** Each structure steps away from every water until its footprint (`size(id)` from its centre) is dry. */
export function clearStructuresOffWater(
  pos: Positions,
  ids: readonly string[],
  size: (id: string) => number,
  waters: readonly WaterPath[]
): void {
  for (const id of ids) {
    const p = pos.get(id);
    if (!p) continue;
    const reach = size(id);
    for (let pass = 0; pass < 12; pass++) {
      const push = new Vector3();
      for (const w of waters) {
        const near = nearestWater(p, [w]);
        const clear = reach - near.distance;
        if (clear <= 0) continue;
        push.addScaledVector(directionOr(p.clone().sub(near.centre).setY(0), near.normal), clear + 0.2);
      }
      if (push.lengthSq() < 1e-6) break;
      p.add(push);
    }
  }
}

/** The positions of the nodes linked to `id`. */
export function neighbours(pos: Positions, links: readonly LinkDescription[], id: string): Vector3[] {
  return links
    .flatMap(l => (l.from === id ? [l.to] : l.to === id ? [l.from] : []))
    .map(n => pos.get(n))
    .filter(q => q !== undefined);
}

/**
 * Manholes that fall in or right next to a water move onto the bank, on the side their pipes come
 * from (or the side they were on). Returns the ones that moved.
 */
export function moveJunctionsOffWater(
  pos: Positions,
  links: readonly LinkDescription[],
  structures: ReadonlySet<string>,
  waters: readonly WaterPath[]
): Set<string> {
  const moved = new Set<string>();
  for (let pass = 0; pass < 2; pass++) {
    for (const [id, p] of pos) {
      if (structures.has(id)) continue;
      const near = nearestWater(p, waters);
      if (near.distance > 1.2) continue;
      const pull = neighbours(pos, links, id).reduce((a, q) => a + q.clone().sub(near.centre).dot(near.normal), 0);
      const own = p.clone().sub(near.centre).dot(near.normal);
      const side = Math.sign(Math.abs(pull) > 0.5 ? pull : own) || 1;
      p.copy(near.centre).addScaledVector(near.normal, side * (near.water.width / 2 + 1.3));
      moved.add(id);
    }
  }
  return moved;
}

/** Moved manholes that ended up on top of each other spread along the bank. */
export function spreadAlongBank(pos: Positions, moved: ReadonlySet<string>, waters: readonly WaterPath[]): void {
  for (let pass = 0; pass < 4; pass++) {
    for (const a of moved) {
      for (const b of moved) {
        const p = pos.get(a);
        const q = pos.get(b);
        if (!p || !q || a >= b || p.distanceTo(q) > 1.6) continue;
        const near = nearestWater(p, waters);
        const along = new Vector3(near.normal.z, 0, -near.normal.x);
        const offset = q.clone().sub(p).dot(along);
        const d = offset >= 0 ? 1 : -1;
        const gap = (1.7 - Math.abs(offset)) / 2;
        p.addScaledVector(along, -d * gap);
        q.addScaledVector(along, d * gap);
      }
    }
  }
}

/** The plant beside the outfall steps back from the water until its yard is dry. */
export function clearPlantOffWater(outfall: Vector3, waters: readonly WaterPath[]): void {
  for (let pass = 0; pass < 6; pass++) {
    const centre = outfall.clone().add(new Vector3(PLANT_SITE.x + PLANT_MIDDLE.x, 0, PLANT_SITE.z + PLANT_MIDDLE.z));
    const near = nearestWater(centre, waters);
    const clear = PLANT_RADIUS - near.distance;
    if (clear <= 0) break;
    outfall.addScaledVector(directionOr(centre.sub(near.centre).setY(0), new Vector3(0, 0, -1)), clear + 0.3);
  }
}
