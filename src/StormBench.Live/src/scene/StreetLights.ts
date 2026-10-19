import {
  Color,
  type InstancedMesh as InstancedMeshType,
  InstancedMesh,
  Matrix4,
  type Material,
  MeshBasicMaterial,
  MeshStandardMaterial,
  type Object3D,
  Quaternion,
  SphereGeometry,
  Vector3
} from 'three';
import type { KitModel, Placement } from '../assets/kit';

const BULB = 0.07;
const OFF = new Color(0xb8bec7);
const ON = new Color(0xffd79a);
/** The warm light of the lamps on the streets themselves at night, as emission over the road tiles' own colours. */
const STREET_GLOW = new Color(0xffbf7a);
const STREET_GLOW_STRENGTH = 0.5;

/** How far a lamp stands from the middle of its road, in tile widths (Streets places them so). */
export const LAMP_OFFSET = 0.45;

/** Where the lamp's light hangs on a kit lamp model: the end of its arm, at the top, farthest from the post. */
export function lampHead(model: KitModel): Vector3 {
  let top = -Infinity;
  for (const part of model.parts) {
    const p = part.geometry.getAttribute('position');
    for (let i = 0; i < p.count; i++) top = Math.max(top, p.getY(i));
  }
  const head = new Vector3();
  let reach = -1;
  for (const part of model.parts) {
    const p = part.geometry.getAttribute('position');
    for (let i = 0; i < p.count; i++) {
      if (p.getY(i) < top * 0.9) continue;
      const r = Math.hypot(p.getX(i), p.getZ(i));
      if (r > reach) {
        reach = r;
        head.set(p.getX(i), p.getY(i), p.getZ(i));
      }
    }
  }
  return head;
}

/**
 * The street lamps at night: the bulb at the end of each lamp's arm lights up warm, and the
 * streets they light take on a soft, even warm light of their own, so a lit street reads as one
 * lit street rather than a row of spots. By day the bulbs are dull glass and the streets as they are.
 */
export class StreetLights {
  private readonly bulbs: InstancedMeshType;
  private readonly bulbMaterial = new MeshBasicMaterial({ color: OFF.clone() });
  private readonly streets: MeshStandardMaterial[];

  constructor(parent: Object3D, model: KitModel, placements: readonly Placement[], streets: readonly Material[]) {
    const head = lampHead(model);
    this.bulbs = new InstancedMesh(new SphereGeometry(BULB, 10, 6), this.bulbMaterial, placements.length);
    const m = new Matrix4();
    const q = new Quaternion();
    const up = new Vector3(0, 1, 0);
    const one = new Vector3(1, 1, 1);
    placements.forEach((pl, i) => {
      const at = head.clone().multiplyScalar(pl.scale).applyAxisAngle(up, pl.rotation).add(pl.position);
      this.bulbs.setMatrixAt(i, m.compose(at.setY(at.y - BULB * 0.6), q, one));
    });
    this.bulbs.castShadow = false;
    this.bulbs.receiveShadow = false;
    parent.add(this.bulbs);
    this.streets = streets.filter((s): s is MeshStandardMaterial => s instanceof MeshStandardMaterial);
    for (const street of this.streets) {
      street.emissive.copy(STREET_GLOW);
      street.emissiveMap = street.map;
      street.emissiveIntensity = 0;
      street.needsUpdate = true;
    }
  }

  /** Lights the lamps by `on` (0 off by day, 1 full at night). */
  light(on: number): void {
    this.bulbMaterial.color.copy(OFF).lerp(ON, on);
    for (const street of this.streets) street.emissiveIntensity = STREET_GLOW_STRENGTH * on;
  }
}
