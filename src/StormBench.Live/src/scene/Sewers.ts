import { Vector3 } from 'three';
import { place } from '../assets/structures';
import { TILE } from '../config';
import type { SceneContext } from './context';

/** How far from the middle of the road a manhole cover lies: in the lane. */
const LANE = 0.38;

/**
 * The sewers lie under the ground and are not drawn. Their manhole covers show where they do in a
 * town, in the street: one in the lane of the straight road over each junction of the network
 * that lies under a street, none in the fields, on the banks or on a bend.
 */
export function buildManholes(
  { scene, world, structures }: SceneContext,
  straights: readonly { readonly at: Vector3; readonly alongX: boolean }[]
): void {
  const net = world.network;
  const joints = new Set<string>();
  for (const link of net.links) {
    if (!world.pos.has(link.from) || !world.pos.has(link.to)) continue;
    if (!world.tankSet.has(link.from)) joints.add(link.from);
    if (!world.tankSet.has(link.to)) joints.add(link.to);
  }
  const used = new Set<object>();
  for (const id of joints) {
    if (id === net.wwtp || net.overflowPoints.includes(id)) continue;
    const p = world.pos.get(id);
    if (!p) continue;
    let best: (typeof straights)[number] | null = null;
    for (const s of straights) if (!best || s.at.distanceTo(p) < best.at.distanceTo(p)) best = s;
    if (!best || best.at.distanceTo(p) > TILE * 1.5 || used.has(best)) continue;
    used.add(best);
    const side = best.alongX ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
    const lane = Math.sign(p.clone().sub(best.at).dot(side)) || 1;
    const manhole = place(structures.manhole);
    manhole.position
      .copy(best.at)
      .addScaledVector(side, lane * LANE)
      .setY(0.03);
    scene.add(manhole);
  }
}
