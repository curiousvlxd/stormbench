import {
  type BufferGeometry,
  type CanvasTexture,
  type Material,
  Mesh,
  MeshStandardMaterial,
  type Object3D,
  type Scene,
  type Texture
} from 'three';
import type { Kits } from '../assets/kit';
import type { Palette } from '../assets/materials';
import { meshOf, ownMaterial, type Structures } from '../assets/structures';
import { ALARM_COLOR } from '../config';
import type { AlarmLevel } from '../types';
import type { Labels } from '../ui/Labels';
import type { Xray } from '../render/Xray';
import type { Picking } from '../ui/Picking';
import type { WaterSurfaces } from '../world/WaterSurfaces';
import type { World } from '../world/World';

export interface SharedTextures {
  readonly waterNormals: Texture;
  readonly glow: CanvasTexture;
  readonly stripes: CanvasTexture;
}

export interface SceneContext {
  readonly scene: Scene;
  readonly xray: Xray;
  readonly world: World;
  readonly palette: Palette;
  readonly textures: SharedTextures;
  readonly kits: Kits;
  readonly structures: Structures;
  readonly labels: Labels;
  readonly picking: Picking;
  readonly surfaces: WaterSurfaces;
  readonly rand: () => number;
}

export interface MeshOptions {
  readonly shadow?: boolean;
  readonly receive?: boolean;
}

/** A mesh added to `parent`, casting and receiving shadows unless told otherwise. */
export function addMesh<M extends Material>(
  parent: Object3D,
  geometry: BufferGeometry,
  material: M,
  { shadow = true, receive = true }: MeshOptions = {}
): Mesh<BufferGeometry, M> {
  const mesh = new Mesh(geometry, material);
  mesh.castShadow = shadow;
  mesh.receiveShadow = receive;
  parent.add(mesh);
  return mesh;
}

/** A copy of a shared texture that can scroll and repeat on its own. */
export function own<T extends Texture>(texture: T): T {
  const copy = texture.clone();
  copy.needsUpdate = true;
  return copy;
}

const LAMP_OFF = 0x6b5052;

/** The alarm lamp of a model (the part with role `beacon`): dark glass while all is well. */
export function alarmLamp(part: Object3D): MeshStandardMaterial {
  return ownMaterial(meshOf(part), new MeshStandardMaterial({ color: LAMP_OFF, roughness: 0.3, emissive: 0x000000 }));
}

/** Lights an alarm lamp in the alarm's colour and pulses it, or turns it off. */
export function pulseLamp(lamp: MeshStandardMaterial, alarm: AlarmLevel | null, t: number): void {
  if (!alarm) {
    lamp.color.setHex(LAMP_OFF);
    lamp.emissiveIntensity = 0;
    return;
  }
  const color = ALARM_COLOR[alarm] ?? ALARM_COLOR.high;
  lamp.color.setHex(color);
  lamp.emissive.setHex(color);
  lamp.emissiveIntensity = 0.7 + 0.7 * Math.max(0, Math.sin(t * 6));
}
