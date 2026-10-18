import * as THREE from '../lib/three/three.module.min.js';
import { GLTFLoader } from '../lib/three/addons/loaders/GLTFLoader.js';
import { mergeGeometries } from '../lib/three/addons/utils/BufferGeometryUtils.js';

const ROOT = 'live/kenney/';

const KITS = {
    houses: { dir: 'suburban', names: 'abcdefghijklmnopqrstu'.split('').map(c => `building-type-${c}`) },
    centre: { dir: 'commercial', names: 'abcdefghijklmn'.split('').map(c => `building-${c}`) },
    towers: { dir: 'commercial', names: ['building-skyscraper-a', 'building-skyscraper-b'] },
    industry: { dir: 'industrial', names: ['building-a', 'building-c', 'building-e', 'building-h', 'building-k', 'building-m'] },
    plant: { dir: 'industrial', names: ['detail-tank', 'detail-tank-large', 'chimney-medium', 'water-tower'] },
    trees: { dir: 'nature', names: ['tree_default', 'tree_oak', 'tree_detailed', 'tree_cone', 'tree_fat', 'tree_pineRoundA', 'tree_pineRoundC'] },
    bushes: { dir: 'nature', names: ['plant_bush', 'plant_bushLarge', 'plant_bushDetailed'] },
    roads: { dir: 'roads', names: ['road-straight', 'road-crossroad', 'road-intersection', 'road-bend', 'road-end-round', 'road-crossing'], byName: true },
    lamps: { dir: 'roads', names: ['light-square'], origin: true },
};

/** A loaded model: parts by material, bounding size. */
function prototype(gltf, origin = false) {
    const byMaterial = new Map();
    gltf.scene.updateMatrixWorld(true);
    gltf.scene.traverse(o => {
        if (!o.isMesh) return;
        const g = o.geometry.clone().applyMatrix4(o.matrixWorld);
        for (const name of Object.keys(g.attributes)) {
            if (!['position', 'normal', 'uv'].includes(name)) g.deleteAttribute(name);
        }
        if (!g.attributes.uv) g.setAttribute('uv', new THREE.Float32BufferAttribute(new Float32Array(g.attributes.position.count * 2), 2));
        const key = o.material.uuid;
        if (!byMaterial.has(key)) byMaterial.set(key, { material: o.material, geometries: [] });
        byMaterial.get(key).geometries.push(g.index ? g.toNonIndexed() : g);
    });
    const parts = [...byMaterial.values()].map(({ material, geometries }) => {
        const m = material.clone();
        if (m.map) m.map.colorSpace = THREE.SRGBColorSpace;
        m.roughness = Math.max(m.roughness ?? 1, 0.7);
        m.metalness = 0;
        return { geometry: mergeGeometries(geometries, false), material: m };
    });
    const box = new THREE.Box3();
    for (const p of parts) {
        p.geometry.computeBoundingBox();
        box.union(p.geometry.boundingBox);
    }
    const centre = box.getCenter(new THREE.Vector3());
    if (origin) centre.set(0, 0, 0);
    for (const p of parts) p.geometry.translate(-centre.x, -box.min.y, -centre.z);
    const size = box.getSize(new THREE.Vector3());
    return { parts, size };
}

/** All kits; a kit that fails to load is empty and the scene draws without it. */
export async function loadKits() {
    const loader = new GLTFLoader();
    const kits = {};
    await Promise.all(Object.entries(KITS).map(async ([key, kit]) => {
        const models = await Promise.all(kit.names.map(name =>
            loader.loadAsync(`${ROOT}${kit.dir}/${name}.glb`).then(g => prototype(g, kit.origin)).catch(() => null)));
        kits[key] = kit.byName
            ? Object.fromEntries(kit.names.map((name, i) => [name, models[i]]).filter(([, m]) => m))
            : models.filter(Boolean);
    }));
    const textures = new THREE.TextureLoader();
    const greens = [0x4f9e3a, 0x3d8a3e, 0x67b043, 0x2f7d45, 0x7cb342];
    for (const [i, proto] of [...(kits.trees ?? []), ...(kits.bushes ?? [])].entries()) {
        for (const part of proto.parts) {
            if (!part.material.map && /leaf|leaves|bush|plant/i.test(part.material.name)) part.material.color.setHex(greens[i % greens.length]);
        }
    }
    kits.houseVariations = (await Promise.all(['variation-a', 'variation-b', 'variation-c'].map(v =>
        textures.loadAsync(`${ROOT}suburban/Textures/${v}.png`).catch(() => null)))).filter(Boolean);
    for (const t of kits.houseVariations) {
        t.colorSpace = THREE.SRGBColorSpace;
        t.flipY = false;
        t.magFilter = THREE.NearestFilter;
    }
    return kits;
}

/**
 * Draws copies of one prototype: `placements` are { position, rotation (y, radians), scale }.
 * `map` replaces the colour map of the parts that have one (house colour variations).
 */
export function instance(parent, proto, placements, { map = null, shadow = true } = {}) {
    if (!placements.length) return [];
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), up = new THREE.Vector3(0, 1, 0), s = new THREE.Vector3();
    return proto.parts.map(part => {
        let material = part.material;
        if (map && material.map) {
            material = material.clone();
            material.map = map;
        }
        const mesh = new THREE.InstancedMesh(part.geometry, material, placements.length);
        placements.forEach((p, i) => {
            q.setFromAxisAngle(up, p.rotation ?? 0);
            s.setScalar(p.scale ?? 1);
            m.compose(p.position, q, s);
            mesh.setMatrixAt(i, m);
        });
        mesh.castShadow = shadow;
        mesh.receiveShadow = true;
        parent.add(mesh);
        return mesh;
    });
}
