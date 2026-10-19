import { Group } from 'three';
import { flat } from '../assets/materials';
import { box, cylinder, merge } from '../models/primitives';
import { placeKind } from '../ui/labelText';
import { addMesh, type SceneContext } from './context';

/** Place names and rain gauges: the labels and symbols of the benchmark's map. */
export function buildPlaces({ scene, world, labels }: SceneContext): void {
  for (const pl of world.places) labels.add(pl.name, pl.at, 0.4, `place ${placeKind(pl.name)}`, null);
  const mast = merge([
    cylinder(0.05, 0.06, 2.2, 0, 1.1, 0, 8),
    cylinder(0.32, 0.22, 0.42, 0, 2.35, 0, 12),
    box(0.5, 0.35, 0.35, 0, 0.6, 0.22, 0.03)
  ]);
  const white = flat(0xf4f6f8);
  const base = flat(0x7b8794);
  for (const g of world.gauges) {
    const group = new Group();
    group.position.copy(g.at);
    addMesh(group, mast, white);
    addMesh(group, box(1.2, 0.12, 1.2, 0, 0.06, 0), base);
    scene.add(group);
    labels.add(`${g.id} rain gauge`, g.at, 3.2, 'gauge', null);
  }
}
