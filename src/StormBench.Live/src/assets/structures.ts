import { type Material, Mesh, type Object3D, Vector3 } from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';
import { MODELS } from '../config';

const FILES = {
  tank: 'tank',
  gateControlled: 'gate-controlled',
  gateFixed: 'gate-fixed',
  weirChamber: 'weir-chamber',
  manhole: 'manhole',
  plcCabinet: 'plc-cabinet',
  controlRoom: 'control-room',
  treatmentPlant: 'treatment-plant',
  outfall: 'outfall',
  bridge: 'bridge'
} as const;

export type StructureName = keyof typeof FILES;

/** The structure models from Blender (assets/models), one template per model, copied for each use. */
export type Structures = Readonly<Record<StructureName, Object3D>>;

/** Loads every structure model; the scene cannot be built without them. */
export async function loadStructures(): Promise<Structures> {
  const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  const names = Object.keys(FILES) as StructureName[];
  const scenes = await Promise.all(names.map(name => loader.loadAsync(`${MODELS}${FILES[name]}.glb`)));
  const structures = {} as Record<StructureName, Object3D>;
  names.forEach((name, i) => {
    structures[name] = scenes[i]!.scene;
  });
  return structures;
}

/** A copy of a template, every mesh casting and receiving shadows. */
export function place(template: Object3D): Object3D {
  const copy = template.clone(true);
  copy.traverse(o => {
    if (o instanceof Mesh) {
      o.castShadow = true;
      o.receiveShadow = true;
    }
  });
  return copy;
}

/** The parts of a model that carry `role` (a custom property set in Blender). */
export function withRole(root: Object3D, role: string): Object3D[] {
  const found: Object3D[] = [];
  root.traverse(o => {
    if (o.userData['role'] === role) found.push(o);
  });
  return found;
}

/** The one part of a model with `role`. */
export function roleOf(root: Object3D, role: string): Object3D {
  const [part] = withRole(root, role);
  if (!part) throw new Error(`no part with role ${role} in ${root.name || 'model'}`);
  return part;
}

/** The first mesh at or below `part`. */
export function meshOf(part: Object3D): Mesh {
  let mesh: Mesh | null = null;
  part.traverse(o => {
    if (!mesh && o instanceof Mesh) mesh = o;
  });
  if (!mesh) throw new Error(`no mesh under ${part.name}`);
  return mesh;
}

/** Gives a mesh its own material, so changing it does not change every copy of the model. */
export function ownMaterial<M extends Material>(mesh: Mesh, material: M): M {
  mesh.material = material;
  return material;
}

/** A numeric custom property of a part, as set in Blender. */
export function numberOf(part: Object3D, key: string): number {
  const value: unknown = part.userData[key];
  if (typeof value !== 'number') throw new Error(`${part.name} has no number ${key}`);
  return value;
}

/** Where the part with `role` of a placed model is in the world. */
export function anchorOf(root: Object3D, role: string): Vector3 {
  root.updateWorldMatrix(true, true);
  return roleOf(root, role).getWorldPosition(new Vector3());
}
