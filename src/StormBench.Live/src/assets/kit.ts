import {
  type BufferGeometry,
  Box3,
  Float32BufferAttribute,
  InstancedMesh,
  Matrix4,
  type Mesh,
  type MeshStandardMaterial,
  NearestFilter,
  type Object3D,
  Quaternion,
  SRGBColorSpace,
  type Texture,
  TextureLoader,
  Vector3
} from 'three';
import { type GLTF, GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { KENNEY } from '../config';

export const ROAD_TILES = [
  'road-straight',
  'road-crossroad',
  'road-intersection',
  'road-bend',
  'road-end-round',
  'road-crossing'
] as const;

export type RoadTile = (typeof ROAD_TILES)[number];

export interface KitPart {
  readonly geometry: BufferGeometry;
  readonly material: MeshStandardMaterial;
}

export interface KitModel {
  readonly parts: readonly KitPart[];
  readonly size: Vector3;
}

type ListKit = 'houses' | 'centre' | 'towers' | 'industry' | 'plant' | 'trees' | 'bushes' | 'lamps';

export type Kits = Record<ListKit, KitModel[]> & {
  roads: Partial<Record<RoadTile, KitModel>>;
  houseVariations: Texture[];
};

export interface Placement {
  readonly position: Vector3;
  readonly rotation: number;
  readonly scale: number;
}

interface KitSource {
  readonly dir: string;
  readonly names: readonly string[];
  readonly origin?: boolean;
}

/** One model name per letter of `range`. */
function lettered(range: string, name: (letter: string) => string): string[] {
  return [...range].map(name);
}

const KITS: Readonly<Record<ListKit, KitSource>> = {
  houses: { dir: 'suburban', names: lettered('abcdefghijklmnopqrstu', c => `building-type-${c}`) },
  centre: { dir: 'commercial', names: lettered('abcdefghijklmn', c => `building-${c}`) },
  towers: { dir: 'commercial', names: ['building-skyscraper-a', 'building-skyscraper-b'] },
  industry: {
    dir: 'industrial',
    names: ['building-a', 'building-c', 'building-e', 'building-h', 'building-k', 'building-m']
  },
  plant: { dir: 'industrial', names: ['detail-tank', 'detail-tank-large', 'chimney-medium', 'water-tower'] },
  trees: {
    dir: 'nature',
    names: ['tree_default', 'tree_oak', 'tree_detailed', 'tree_cone', 'tree_fat', 'tree_pineRoundA', 'tree_pineRoundC']
  },
  bushes: { dir: 'nature', names: ['plant_bush', 'plant_bushLarge', 'plant_bushDetailed'] },
  lamps: { dir: 'roads', names: ['light-square'], origin: true }
};

const KEEP = new Set(['position', 'normal', 'uv']);
const GREENS = [0x4f9e3a, 0x3d8a3e, 0x67b043, 0x2f7d45, 0x7cb342];
const VARIATIONS = ['variation-a', 'variation-b', 'variation-c'];

/** No models at all: the scene then draws its own stand-ins. */
export function emptyKits(): Kits {
  return {
    houses: [],
    centre: [],
    towers: [],
    industry: [],
    plant: [],
    trees: [],
    bushes: [],
    lamps: [],
    roads: {},
    houseVariations: []
  };
}

/** A loaded model as one merged geometry per material, standing on y = 0 and centred (or at its own origin). */
function prototype(gltf: GLTF, origin = false): KitModel {
  const byMaterial = new Map<string, { material: MeshStandardMaterial; geometries: BufferGeometry[] }>();
  gltf.scene.updateMatrixWorld(true);
  gltf.scene.traverse((o: Object3D) => {
    if (!(o as Mesh).isMesh) return;
    const mesh = o as Mesh<BufferGeometry, MeshStandardMaterial>;
    const g = mesh.geometry.clone().applyMatrix4(mesh.matrixWorld);
    for (const name of Object.keys(g.attributes)) {
      if (!KEEP.has(name)) g.deleteAttribute(name);
    }
    if (!g.hasAttribute('uv')) {
      g.setAttribute('uv', new Float32BufferAttribute(new Float32Array(g.getAttribute('position').count * 2), 2));
    }
    const key = mesh.material.uuid;
    let entry = byMaterial.get(key);
    if (!entry) {
      entry = { material: mesh.material, geometries: [] };
      byMaterial.set(key, entry);
    }
    entry.geometries.push(g.index ? g.toNonIndexed() : g);
  });
  const parts = [...byMaterial.values()].map(({ material, geometries }) => {
    const m = material.clone();
    if (m.map) m.map.colorSpace = SRGBColorSpace;
    m.roughness = Math.max(m.roughness ?? 1, 0.7);
    m.metalness = 0;
    return { geometry: mergeGeometries(geometries, false), material: m };
  });
  const box = new Box3();
  for (const p of parts) {
    p.geometry.computeBoundingBox();
    if (p.geometry.boundingBox) box.union(p.geometry.boundingBox);
  }
  const centre = box.getCenter(new Vector3());
  if (origin) centre.set(0, 0, 0);
  for (const p of parts) p.geometry.translate(-centre.x, -box.min.y, -centre.z);
  return { parts, size: box.getSize(new Vector3()) };
}

/** The models of a kit in order, null where one fails to load. */
function loadModels(loader: GLTFLoader, kit: KitSource): Promise<(KitModel | null)[]> {
  return Promise.all(
    kit.names.map(name =>
      loader
        .loadAsync(`${KENNEY}${kit.dir}/${name}.glb`)
        .then(g => prototype(g, kit.origin))
        .catch(() => null)
    )
  );
}

/** Paints the leaves of trees and bushes in a few greens, so a wood does not look like one tree. */
function tintLeaves(kits: Kits): void {
  for (const [i, proto] of [...kits.trees, ...kits.bushes].entries()) {
    for (const part of proto.parts) {
      if (!part.material.map && /leaf|leaves|bush|plant/i.test(part.material.name)) {
        part.material.color.setHex(GREENS[i % GREENS.length]!);
      }
    }
  }
}

/** All kits; a model that fails to load is left out and the scene draws without it. */
export async function loadKits(): Promise<Kits> {
  const loader = new GLTFLoader();
  const kits = emptyKits();
  await Promise.all([
    ...(Object.entries(KITS) as [ListKit, KitSource][]).map(async ([key, kit]) => {
      kits[key] = (await loadModels(loader, kit)).filter(m => m !== null);
    }),
    loadModels(loader, { dir: 'roads', names: ROAD_TILES }).then(models =>
      ROAD_TILES.forEach((name, i) => {
        const model = models[i];
        if (model) kits.roads[name] = model;
      })
    )
  ]);
  tintLeaves(kits);
  const textures = new TextureLoader();
  const variations = await Promise.all(
    VARIATIONS.map(v => textures.loadAsync(`${KENNEY}suburban/Textures/${v}.png`).catch(() => null))
  );
  kits.houseVariations = variations.filter(t => t !== null);
  for (const t of kits.houseVariations) {
    t.colorSpace = SRGBColorSpace;
    t.flipY = false;
    t.magFilter = NearestFilter;
  }
  return kits;
}

/**
 * Draws copies of one model, one instanced mesh per part. `map` replaces the colour map of the
 * parts that have one (the house colour variations).
 */
export function instance(
  parent: Object3D,
  proto: KitModel,
  placements: readonly Placement[],
  { map = null, shadow = true }: { map?: Texture | null; shadow?: boolean } = {}
): InstancedMesh[] {
  if (!placements.length) return [];
  const m = new Matrix4();
  const q = new Quaternion();
  const up = new Vector3(0, 1, 0);
  const s = new Vector3();
  return proto.parts.map(part => {
    let material = part.material;
    if (map && material.map) {
      material = material.clone();
      material.map = map;
    }
    const mesh = new InstancedMesh(part.geometry, material, placements.length);
    placements.forEach((p, i) => {
      q.setFromAxisAngle(up, p.rotation);
      s.setScalar(p.scale);
      m.compose(p.position, q, s);
      mesh.setMatrixAt(i, m);
    });
    mesh.castShadow = shadow;
    mesh.receiveShadow = true;
    parent.add(mesh);
    return mesh;
  });
}
