import { BufferAttribute, BufferGeometry, Group, type Object3D, Points, PointsMaterial, Vector3 } from 'three';
import { flat } from '../assets/materials';
import { numberOf, place, roleOf, withRole } from '../assets/structures';
import { PLANT_MIDDLE, PLANT_RADIUS, PLANT_SITE } from '../config';
import type { Label } from '../ui/Labels';
import { addMesh, type SceneContext } from './context';

/**
 * The treatment plant next to the outfall: clarifiers with turning bridges, an aeration basin
 * that bubbles, digesters and a gas holder, and a hall from the industrial kit. The cleaned water leaves through
 * a pipe to its outfall in the river.
 */
export class Plant {
  readonly centre: Vector3 | null = null;
  readonly footprint: { readonly center: Vector3; readonly radius: number } | null = null;
  readonly outlet: Vector3 | null = null;
  readonly label: Label | null = null;
  private readonly bridges: Object3D[] = [];
  private readonly bubbles: Points | null = null;
  private bubbleBase = 0;

  constructor({ scene, world, structures, labels, picking, surfaces, rand }: SceneContext) {
    const p = world.pos.get(world.network.wwtp);
    if (!p) return;
    const group = new Group();
    group.position.copy(p).add(new Vector3(PLANT_SITE.x, 0, PLANT_SITE.z));
    const body = place(structures.treatmentPlant);
    group.add(body);
    this.bridges.push(...withRole(body, 'spin'));
    const waterMat = flat(0x3aa3c4, { roughness: 0.35, emissive: 0x0a3344, emissiveIntensity: 0.25 });
    for (const at of withRole(body, 'water')) {
      const surface =
        at.userData['shape'] === 'disc'
          ? surfaces.disc(numberOf(at, 'radius'), 0.035)
          : surfaces.plane(numberOf(at, 'width'), numberOf(at, 'depth'));
      addMesh(group, surface, waterMat, { shadow: false }).position.copy(at.position);
    }
    const aeration = roleOf(body, 'bubbles');
    this.bubbles = this.aeration(rand, aeration);
    group.add(this.bubbles);
    scene.add(group);
    picking.addModel(group, 'wwtp');
    const centre = group.position.clone().add(new Vector3(PLANT_MIDDLE.x, 0, PLANT_MIDDLE.z));
    this.centre = centre;
    this.label = labels.add('Treatment plant', centre, 4.4, 'plant', 'wwtp');
    for (const dx of [-8, 9]) for (const dz of [-6, 6]) world.extend(centre.clone().add(new Vector3(dx, 0, dz)));

    this.outlet = group.position
      .clone()
      .add(new Vector3(2.2, 0, 3.2))
      .setY(0);
    this.footprint = { center: centre, radius: PLANT_RADIUS };
  }

  /** Turns the clarifier bridges and bubbles the basin, faster while a storm runs. */
  tick(dt: number, t: number, running: boolean): void {
    for (const bridge of this.bridges) bridge.rotation.y += dt * (running ? 0.35 : 0.06);
    if (this.bubbles) {
      const position = this.bubbles.geometry.getAttribute('position');
      const pos = position.array;
      for (let i = 1; i < pos.length; i += 3) pos[i] = this.bubbleBase + ((t * 0.6 + i * 0.137) % 1) * 0.08;
      position.needsUpdate = true;
    }
  }

  /** The bubbles over the aeration basin, scattered once over the area the model marks. */
  private aeration(rand: () => number, at: Object3D): Points {
    const count = numberOf(at, 'count');
    const width = numberOf(at, 'width');
    const depth = numberOf(at, 'depth');
    const bubbles = new Float32Array(count * 3);
    for (let i = 0; i < count; i++) {
      bubbles.set(
        [at.position.x + (rand() - 0.5) * width, at.position.y, at.position.z + (rand() - 0.5) * depth],
        i * 3
      );
    }
    this.bubbleBase = at.position.y;
    const geometry = new BufferGeometry();
    geometry.setAttribute('position', new BufferAttribute(bubbles, 3));
    return new Points(
      geometry,
      new PointsMaterial({ color: 0xcfefff, size: 0.09, transparent: true, opacity: 0.7, depthWrite: false })
    );
  }
}
