import * as THREE from '../lib/three/three.module.min.js';
import { RoundedBoxGeometry } from '../lib/three/addons/geometries/RoundedBoxGeometry.js';
import { mergeGeometries, mergeVertices } from '../lib/three/addons/utils/BufferGeometryUtils.js';

/** Merge parts into one geometry with position, normal and uv only. */
export function merge(parts) {
    const prepared = parts.map(part => {
        const g = part.index ? part.toNonIndexed() : part.clone();
        for (const name of Object.keys(g.attributes)) {
            if (!['position', 'normal', 'uv'].includes(name)) g.deleteAttribute(name);
        }
        if (!g.attributes.normal) g.computeVertexNormals();
        if (!g.attributes.uv) g.setAttribute('uv', new THREE.Float32BufferAttribute(new Float32Array(g.attributes.position.count * 2), 2));
        g.clearGroups();
        return g;
    });
    return mergeGeometries(prepared, false);
}

export function box(w, h, d, x = 0, y = 0, z = 0, radius = 0) {
    const g = radius > 0 ? new RoundedBoxGeometry(w, h, d, 2, radius) : new THREE.BoxGeometry(w, h, d);
    g.translate(x, y, z);
    return g;
}

export function cylinder(rTop, rBottom, h, x = 0, y = 0, z = 0, segments = 16) {
    const g = new THREE.CylinderGeometry(rTop, rBottom, h, segments);
    g.translate(x, y, z);
    return g;
}

/** A cylinder from a to b (Vector3). */
export function rod(a, b, radius, segments = 8) {
    const dir = new THREE.Vector3().subVectors(b, a);
    const g = new THREE.CylinderGeometry(radius, radius, dir.length(), segments);
    const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().normalize());
    g.applyQuaternion(q);
    const mid = a.clone().addScaledVector(dir, 0.5);
    g.translate(mid.x, mid.y, mid.z);
    return g;
}

function canvas(size, draw) {
    const c = document.createElement('canvas');
    c.width = c.height = size;
    draw(c.getContext('2d'), size);
    return c;
}

function texture(c, { repeat = 1, srgb = true } = {}) {
    const t = new THREE.CanvasTexture(c);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.repeat.set(repeat, repeat);
    t.anisotropy = 8;
    if (srgb) t.colorSpace = THREE.SRGBColorSpace;
    return t;
}

function speckle(g, size, rand, count, alpha, light) {
    for (let i = 0; i < count; i++) {
        const v = light ? 255 : 0;
        g.fillStyle = `rgba(${v},${v},${v},${alpha * rand()})`;
        const s = 1 + rand() * 2;
        g.fillRect(rand() * size, rand() * size, s, s);
    }
}

export function makeTextures(rand) {
    const plaster = texture(canvas(256, (g, s) => {
        g.fillStyle = '#f2f0ec';
        g.fillRect(0, 0, s, s);
        speckle(g, s, rand, 5000, 0.10, false);
        speckle(g, s, rand, 2500, 0.12, true);
    }));

    const roof = texture(canvas(256, (g, s) => {
        g.fillStyle = '#e8e4e0';
        g.fillRect(0, 0, s, s);
        const rows = 16, cols = 12;
        for (let r = 0; r < rows; r++) {
            const y = (r / rows) * s;
            const grad = g.createLinearGradient(0, y, 0, y + s / rows);
            grad.addColorStop(0, 'rgba(255,255,255,0.18)');
            grad.addColorStop(1, 'rgba(0,0,0,0.35)');
            g.fillStyle = grad;
            g.fillRect(0, y, s, s / rows);
            const off = (r % 2) * (s / cols / 2);
            g.fillStyle = 'rgba(0,0,0,0.35)';
            for (let c = 0; c <= cols; c++) g.fillRect(off + (c / cols) * s, y, 1.5, s / rows);
        }
        speckle(g, s, rand, 1500, 0.15, false);
    }));

    const concrete = texture(canvas(256, (g, s) => {
        g.fillStyle = '#a7a9ac';
        g.fillRect(0, 0, s, s);
        speckle(g, s, rand, 9000, 0.16, false);
        speckle(g, s, rand, 5000, 0.14, true);
        g.fillStyle = 'rgba(0,0,0,0.12)';
        for (let i = 1; i < 4; i++) g.fillRect(0, (i / 4) * s, s, 1.5);
        for (let i = 0; i < 6; i++) {
            g.fillStyle = `rgba(60,55,50,${0.05 + rand() * 0.06})`;
            g.fillRect(rand() * s, 0, 2 + rand() * 6, s);
        }
    }));

    const asphalt = texture(canvas(256, (g, s) => {
        g.fillStyle = '#3a3c40';
        g.fillRect(0, 0, s, s);
        speckle(g, s, rand, 12000, 0.22, false);
        speckle(g, s, rand, 6000, 0.12, true);
    }));

    const grass = texture(canvas(256, (g, s) => {
        g.fillStyle = '#26402a';
        g.fillRect(0, 0, s, s);
        for (let i = 0; i < 9000; i++) {
            const h = 95 + rand() * 40, l = 14 + rand() * 18;
            g.fillStyle = `hsla(${h},35%,${l}%,0.5)`;
            g.fillRect(rand() * s, rand() * s, 1 + rand() * 2, 2 + rand() * 3);
        }
    }));

    const paving = texture(canvas(128, (g, s) => {
        g.fillStyle = '#8d8f92';
        g.fillRect(0, 0, s, s);
        g.fillStyle = 'rgba(0,0,0,0.3)';
        for (let i = 0; i <= 8; i++) {
            g.fillRect((i / 8) * s, 0, 1, s);
            g.fillRect(0, (i / 8) * s, s, 1);
        }
        speckle(g, s, rand, 1500, 0.12, false);
    }));

    const metal = texture(canvas(128, (g, s) => {
        g.fillStyle = '#c9ccd0';
        g.fillRect(0, 0, s, s);
        for (let i = 0; i < 400; i++) {
            g.fillStyle = `rgba(255,255,255,${rand() * 0.08})`;
            g.fillRect(0, rand() * s, s, 1);
        }
        speckle(g, s, rand, 600, 0.1, false);
    }));

    return { plaster, roof, concrete, asphalt, grass, paving, metal, waterNormals: waterNormalTexture() };
}

/** A tileable normal map of small waves, from a sum of sines on integer frequencies. */
function waterNormalTexture() {
    const size = 256;
    const c = document.createElement('canvas');
    c.width = c.height = size;
    const g = c.getContext('2d');
    const img = g.createImageData(size, size);
    const waves = [];
    const rand = (() => { let a = 7; return () => ((a = (a * 16807) % 2147483647) / 2147483647); })();
    for (let i = 0; i < 18; i++) {
        waves.push({ kx: Math.round((rand() - 0.5) * 16), ky: Math.round((rand() - 0.5) * 16), a: 0.6 / (1 + i * 0.35), p: rand() * Math.PI * 2 });
    }
    for (let y = 0; y < size; y++) {
        for (let x = 0; x < size; x++) {
            let dx = 0, dy = 0;
            const u = (x / size) * Math.PI * 2, v = (y / size) * Math.PI * 2;
            for (const w of waves) {
                const c0 = Math.cos(w.kx * u + w.ky * v + w.p) * w.a;
                dx += c0 * w.kx;
                dy += c0 * w.ky;
            }
            const n = new THREE.Vector3(-dx * 0.05, -dy * 0.05, 1).normalize();
            const o = (y * size + x) * 4;
            img.data[o] = (n.x * 0.5 + 0.5) * 255;
            img.data[o + 1] = (n.y * 0.5 + 0.5) * 255;
            img.data[o + 2] = (n.z * 0.5 + 0.5) * 255;
            img.data[o + 3] = 255;
        }
    }
    g.putImageData(img, 0, 0);
    const t = new THREE.CanvasTexture(c);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    return t;
}

export function glowTexture() {
    const c = canvas(64, (g) => {
        const gradient = g.createRadialGradient(32, 32, 0, 32, 32, 32);
        gradient.addColorStop(0, 'rgba(255,255,255,1)');
        gradient.addColorStop(0.3, 'rgba(255,255,255,0.55)');
        gradient.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = gradient;
        g.fillRect(0, 0, 64, 64);
    });
    return new THREE.CanvasTexture(c);
}

export function makeMaterials(tex) {
    const concrete = new THREE.MeshStandardMaterial({ color: 0xd2d3d3, map: tex.concrete, roughness: 0.88, metalness: 0 });
    const concreteDark = new THREE.MeshStandardMaterial({ color: 0x7d8186, map: tex.concrete, roughness: 0.92 });
    const steel = new THREE.MeshStandardMaterial({ color: 0xaeb5bd, map: tex.metal, roughness: 0.35, metalness: 0.85 });
    const darkSteel = new THREE.MeshStandardMaterial({ color: 0x4a525c, roughness: 0.45, metalness: 0.8 });
    const paint = new THREE.MeshStandardMaterial({ color: 0x2f6f9f, roughness: 0.45, metalness: 0.35 });
    const yellow = new THREE.MeshStandardMaterial({ color: 0xe0b020, roughness: 0.5, metalness: 0.2 });
    const castIron = new THREE.MeshStandardMaterial({ color: 0x2c2f33, roughness: 0.6, metalness: 0.7 });
    concrete.side = THREE.DoubleSide;
    return { concrete, concreteDark, steel, darkSteel, paint, yellow, castIron };
}

/** A window on a facade: centre in house space, facing direction (radians around y), size. */
function win(x, y, z, ry, w = 0.26, h = 0.32) {
    return { x, y, z, ry, w, h };
}

function frames(windows, depth = 0.035) {
    const parts = [];
    for (const w of windows) {
        const bar = 0.04;
        const pieces = [
            box(w.w + bar * 2, bar, depth, 0, w.h / 2 + bar / 2, 0),
            box(w.w + bar * 2, bar, depth, 0, -w.h / 2 - bar / 2, 0),
            box(bar, w.h, depth, w.w / 2 + bar / 2, 0, 0),
            box(bar, w.h, depth, -w.w / 2 - bar / 2, 0, 0),
            box(bar * 0.6, w.h, depth * 0.6, 0, 0, 0),
            box(w.w + 0.14, 0.035, depth + 0.05, 0, -w.h / 2 - 0.06, 0.02),
        ];
        for (const g of pieces) {
            g.rotateY(w.ry);
            g.translate(w.x, w.y, w.z);
            parts.push(g);
        }
    }
    return parts;
}

/** Windows on the four facades of a W x D box, rows at the given heights. */
function facadeWindows(W, D, rows, perFront, perSide, size) {
    const list = [];
    for (const y of rows) {
        for (let i = 0; i < perFront; i++) {
            const x = (i - (perFront - 1) / 2) * (W / perFront);
            list.push(win(x, y, D / 2 + 0.012, 0, size.w, size.h));
            list.push(win(-x, y, -D / 2 - 0.012, Math.PI, size.w, size.h));
        }
        for (let i = 0; i < perSide; i++) {
            const z = (i - (perSide - 1) / 2) * (D / perSide);
            list.push(win(W / 2 + 0.012, y, -z, Math.PI / 2, size.w, size.h));
            list.push(win(-W / 2 - 0.012, y, z, -Math.PI / 2, size.w, size.h));
        }
    }
    return list;
}

function gableRoof(W, D, rise, overhang) {
    const shape = new THREE.Shape();
    shape.moveTo(-W / 2 - overhang, 0);
    shape.lineTo(W / 2 + overhang, 0);
    shape.lineTo(0, rise);
    shape.closePath();
    const g = new THREE.ExtrudeGeometry(shape, { depth: D + overhang * 2, bevelEnabled: false });
    g.translate(0, 0, -(D + overhang * 2) / 2);
    return g;
}

/** The house types: walls, roof and trim geometry (base at y = 0, centred) and their windows. */
export function houseTypes() {
    const types = [];

    {
        const W = 1.6, D = 1.3, H = 1.05;
        const windows = [
            ...facadeWindows(W, D, [0.62], 2, 1, { w: 0.28, h: 0.34 }),
            win(W / 2 + 0.012, H + 0.18, 0, Math.PI / 2, 0.2, 0.22),
            win(-W / 2 - 0.012, H + 0.18, 0, -Math.PI / 2, 0.2, 0.22),
        ].filter(w => !(w.ry === 0 && Math.abs(w.x) < 0.1));
        types.push({
            name: 'gable', weight: 0.42, footprint: [W + 0.3, D + 0.3],
            walls: merge([box(W, H, D, 0, H / 2, 0, 0.02)]),
            roof: merge([(() => { const g = gableRoof(W, D, 0.62, 0.12); g.translate(0, H, 0); return g; })()]),
            trim: merge([
                box(0.18, 0.55, 0.18, W * 0.22, H + 0.48, -D * 0.15),
                box(0.3, 0.52, 0.05, 0, 0.26, D / 2 + 0.02),
                box(0.5, 0.04, 0.3, 0, 0.56, D / 2 + 0.15),
                ...frames(windows),
            ]),
            windows,
        });
    }

    {
        const W = 1.5, D = 1.5, H = 1.7;
        const windows = facadeWindows(W, D, [0.5, 1.25], 2, 2, { w: 0.26, h: 0.32 })
            .filter(w => !(w.ry === 0 && w.y < 0.8 && w.x > 0));
        const roof = new THREE.ConeGeometry(W * 0.82, 0.75, 4, 1);
        roof.rotateY(Math.PI / 4);
        roof.translate(0, H + 0.375, 0);
        types.push({
            name: 'hip', weight: 0.3, footprint: [W + 0.3, D + 0.3],
            walls: merge([box(W, H, D, 0, H / 2, 0, 0.02)]),
            roof: merge([roof]),
            trim: merge([
                box(W + 0.04, 0.05, D + 0.04, 0, 0.88, 0),
                box(0.28, 0.5, 0.05, W * 0.25, 0.25, D / 2 + 0.02),
                box(0.16, 0.45, 0.16, -W * 0.2, H + 0.6, W * 0.15),
                ...frames(windows),
            ]),
            windows,
        });
    }

    for (const floors of [3, 5]) {
        const W = 2.4, D = 1.5, fh = 0.52, H = floors * fh + 0.12;
        const rows = Array.from({ length: floors }, (_, i) => 0.3 + i * fh);
        const windows = facadeWindows(W, D, rows, 5, 2, { w: 0.26, h: 0.3 });
        const balconies = [];
        for (let i = 1; i < floors; i++) {
            for (const x of [-0.72, 0.72]) balconies.push(box(0.5, 0.04, 0.24, x, rows[i] - 0.2, D / 2 + 0.12), box(0.5, 0.16, 0.02, x, rows[i] - 0.11, D / 2 + 0.235));
        }
        types.push({
            name: `block${floors}`, weight: floors === 3 ? 0.18 : 0.1, footprint: [W + 0.4, D + 0.4], urban: true,
            walls: merge([box(W, H, D, 0, H / 2, 0, 0.02)]),
            roof: merge([box(W - 0.1, 0.04, D - 0.1, 0, H + 0.02, 0)]),
            trim: merge([
                box(W + 0.04, 0.12, 0.06, 0, H + 0.06, D / 2), box(W + 0.04, 0.12, 0.06, 0, H + 0.06, -D / 2),
                box(0.06, 0.12, D, W / 2, H + 0.06, 0), box(0.06, 0.12, D, -W / 2, H + 0.06, 0),
                box(0.5, 0.3, 0.4, -0.6, H + 0.15, 0.1, 0.02), box(0.35, 0.22, 0.3, 0.5, H + 0.11, -0.2, 0.02),
                box(0.4, 0.62, 0.06, 0, 0.31, D / 2 + 0.03),
                ...balconies,
                ...frames(windows, 0.025),
            ]),
            windows,
        });
    }
    return types;
}

export function treeTypes(rand) {
    const noise = (v) => Math.sin(v.x * 7.1 + v.y * 3.3) * Math.cos(v.z * 5.7 - v.y * 2.1);
    const blob = (r, x, y, z) => {
        let g = new THREE.IcosahedronGeometry(r, 4);
        g.deleteAttribute('normal');
        g.deleteAttribute('uv');
        g = mergeVertices(g);
        const p = g.attributes.position, v = new THREE.Vector3();
        for (let i = 0; i < p.count; i++) {
            v.fromBufferAttribute(p, i);
            v.multiplyScalar(1 + noise(v.clone().multiplyScalar(2.2)) * 0.12 + (rand() - 0.5) * 0.05);
            p.setXYZ(i, v.x, v.y, v.z);
        }
        g.computeVertexNormals();
        g.translate(x, y, z);
        return g;
    };
    const deciduous = {
        trunk: merge([cylinder(0.06, 0.1, 0.9, 0, 0.45, 0, 10), rod(new THREE.Vector3(0, 0.6, 0), new THREE.Vector3(0.22, 1.0, 0.05), 0.04, 6)]),
        crown: merge([blob(0.5, 0, 1.25, 0), blob(0.38, 0.3, 1.1, 0.1), blob(0.36, -0.25, 1.15, -0.15), blob(0.32, 0.05, 1.55, 0.05)]),
    };
    const conifer = {
        trunk: merge([cylinder(0.05, 0.08, 0.5, 0, 0.25, 0, 8)]),
        crown: merge([
            cylinder(0, 0.55, 0.8, 0, 0.75, 0, 18), cylinder(0, 0.44, 0.7, 0, 1.15, 0, 18), cylinder(0, 0.3, 0.6, 0, 1.55, 0, 18),
        ]),
    };
    return [deciduous, conifer];
}

/**
 * A round concrete storage tank, cut open towards +z so the water inside shows.
 * Returns geometries in tank space (base at y = 0): wall (concrete), floor, rail (steel),
 * ladder (steel), gauge (ticks per metre on the cut face), sensor (level sensor on a bracket),
 * plus the opening angles.
 */
export function tankModel(R, H, depthM, thresholdM) {
    const T = 0.32, cut = 1.15;
    const phiStart = cut / 2, phiLength = Math.PI * 2 - cut;
    const profile = [
        new THREE.Vector2(R, 0), new THREE.Vector2(R + T, 0), new THREE.Vector2(R + T, H),
        new THREE.Vector2(R + T + 0.1, H), new THREE.Vector2(R + T + 0.1, H + 0.14),
        new THREE.Vector2(R - 0.06, H + 0.14), new THREE.Vector2(R - 0.06, H), new THREE.Vector2(R, H), new THREE.Vector2(R, 0),
    ];
    const wallParts = [new THREE.LatheGeometry(profile, 96, phiStart, phiLength)];
    for (const phi of [phiStart, phiStart + phiLength]) {
        const shape = new THREE.Shape(profile.slice(0, -1).map(p => new THREE.Vector2(p.x, p.y)));
        const face = new THREE.ShapeGeometry(shape);
        face.rotateY(phi - Math.PI / 2);
        wallParts.push(face);
    }
    const floor = new THREE.CircleGeometry(R + T, 64);
    floor.rotateX(-Math.PI / 2);
    floor.translate(0, 0.02, 0);
    wallParts.push(floor);

    const railParts = [];
    const railR = R + T / 2;
    for (const h of [0.5, 0.26]) {
        const rail = new THREE.TorusGeometry(railR, h === 0.5 ? 0.03 : 0.02, 6, 120, phiLength);
        rail.rotateX(Math.PI / 2);
        rail.rotateY(phiStart + phiLength - Math.PI / 2);
        rail.translate(0, H + 0.14 + h, 0);
        railParts.push(rail);
    }
    const posts = 32;
    for (let i = 0; i <= posts; i++) {
        const phi = phiStart + (i / posts) * phiLength;
        const x = Math.sin(phi) * railR, z = Math.cos(phi) * railR;
        railParts.push(cylinder(0.022, 0.022, 0.52, x, H + 0.14 + 0.26, z, 6));
    }
    const ladderParts = [];
    const lp = phiStart + 0.35;
    const lr = R + T + 0.12;
    const side = new THREE.Vector3(Math.cos(lp), 0, -Math.sin(lp)).multiplyScalar(0.18);
    const base = new THREE.Vector3(Math.sin(lp) * lr, 0, Math.cos(lp) * lr);
    for (const s of [-1, 1]) {
        const a = base.clone().addScaledVector(side, s), b = a.clone().setY(H + 0.7);
        ladderParts.push(rod(a, b, 0.022, 6));
    }
    for (let y = 0.3; y < H + 0.4; y += 0.3) {
        ladderParts.push(rod(base.clone().addScaledVector(side, -1).setY(y), base.clone().addScaledVector(side, 1).setY(y), 0.015, 5));
    }
    const gaugeParts = [];
    const gp = phiStart - 0.002;
    for (let m = 0; m <= depthM; m++) {
        const y = (m / depthM) * H;
        const len = m === thresholdM ? 0.3 : 0.16;
        const tick = box(len, 0.035, 0.01, 0, 0, 0);
        tick.translate(R + len / 2, y, 0);
        tick.rotateY(gp - Math.PI / 2);
        gaugeParts.push(tick);
    }
    const sp = phiStart + phiLength * 0.18;
    const sensorAt = new THREE.Vector3(Math.sin(sp) * (R - 0.35), H + 0.42, Math.cos(sp) * (R - 0.35));
    const bracketFrom = new THREE.Vector3(Math.sin(sp) * (R + T / 2), H + 0.42, Math.cos(sp) * (R + T / 2));
    const sensor = merge([
        rod(bracketFrom, sensorAt, 0.03, 6),
        box(0.16, 0.16, 0.16, sensorAt.x, sensorAt.y + 0.04, sensorAt.z, 0.02),
        cylinder(0.05, 0.1, 0.14, sensorAt.x, sensorAt.y - 0.1, sensorAt.z, 14),
    ]);
    return {
        wall: merge(wallParts),
        rail: merge(railParts),
        ladder: merge(ladderParts),
        gauge: merge(gaugeParts),
        sensor,
        sensorAt,
        phiStart, phiLength, thickness: T,
    };
}

/**
 * A penstock on its chamber: the pipe runs through a concrete chamber, the plate (w x h, across
 * the pipe) slides in guides out of it, a rising spindle runs up to the actuator on the beam.
 * Closed, the plate sits on the chamber floor with its top showing; open, it stands clear above.
 * `plateBase` is the plate's centre height when closed, `travel` how far it rises when open.
 */
export function gateModel(w, h) {
    const floor = 0.05, chamber = floor + 0.75 * h, travel = 0.95 * h;
    const beam = floor + h + travel + 0.12;
    const spindle = beam + 0.5 - (floor + h);
    return {
        concrete: merge([box(w + 0.6, chamber, 1.1, 0, chamber / 2, 0, 0.04)]),
        steel: merge([
            box(0.1, beam - chamber, 0.16, -w / 2 - 0.03, (chamber + beam) / 2, 0),
            box(0.1, beam - chamber, 0.16, w / 2 + 0.03, (chamber + beam) / 2, 0),
            box(w + 0.36, 0.12, 0.2, 0, beam, 0, 0.02),
            box(w + 0.12, 0.012, 0.14, 0, chamber + 0.006, 0),
            cylinder(0.05, 0.05, travel + 0.1, 0, beam + 0.3 + (travel + 0.1) / 2, 0, 10),
        ]),
        paint: merge([
            box(0.34, 0.24, 0.28, 0, beam + 0.18, 0, 0.03),
            (() => { const g = new THREE.CylinderGeometry(0.09, 0.09, 0.18, 14); g.rotateZ(Math.PI / 2); g.translate(0.26, beam + 0.18, 0); return g; })(),
        ]),
        wheel: (() => {
            const z = 0.24, y = beam + 0.18;
            const rim = new THREE.TorusGeometry(0.16, 0.022, 6, 24);
            rim.translate(0, y, z);
            const hub = new THREE.CylinderGeometry(0.035, 0.035, 0.05, 12);
            hub.rotateX(Math.PI / 2);
            hub.translate(0, y, z);
            const spokes = [0, 1].map(i => {
                const g = new THREE.BoxGeometry(0.32, 0.022, 0.022);
                g.rotateZ(i * Math.PI / 2 + Math.PI / 4);
                g.translate(0, y, z);
                return g;
            });
            const shaft = new THREE.CylinderGeometry(0.018, 0.018, z - 0.14 + 0.02, 8);
            shaft.rotateX(Math.PI / 2);
            shaft.translate(0, y, (0.14 + z) / 2);
            return merge([rim, hub, ...spokes, shaft]);
        })(),
        plate: merge([
            box(w - 0.06, h, 0.07, 0, 0, 0, 0.015),
            cylinder(0.025, 0.025, spindle, 0, h / 2 + spindle / 2, 0, 8),
        ]),
        plateBase: floor + h / 2,
        travel,
        top: beam + 0.45,
    };
}

export function chamberModel() {
    const cover = cylinder(0.42, 0.42, 0.05, 0, 0.83, 0, 32);
    const ring = new THREE.TorusGeometry(0.42, 0.04, 8, 32);
    ring.rotateX(Math.PI / 2);
    ring.translate(0, 0.84, 0);
    return {
        concrete: merge([box(2.0, 0.8, 2.0, 0, 0.4, 0, 0.06)]),
        iron: merge([cover, ring, ...[-0.2, 0, 0.2].map(x => box(0.5, 0.012, 0.03, x * 0.5, 0.86, x))]),
        kiosk: merge([box(0.5, 0.9, 0.42, 0.62, 1.25, -0.62, 0.03), box(0.56, 0.05, 0.48, 0.62, 1.72, -0.62)]),
    };
}

export function clarifierModel(R) {
    const profile = [new THREE.Vector2(R, 0), new THREE.Vector2(R + 0.22, 0), new THREE.Vector2(R + 0.22, 0.85), new THREE.Vector2(R, 0.85), new THREE.Vector2(R, 0)];
    return {
        wall: merge([new THREE.LatheGeometry(profile, 72), cylinder(0.25, 0.3, 1.1, 0, 0.55, 0, 16)]),
        bridge: merge([
            box(R * 2 + 0.3, 0.08, 0.32, 0, 1.0, 0),
            box(R * 2 + 0.3, 0.03, 0.03, 0, 1.3, 0.16), box(R * 2 + 0.3, 0.03, 0.03, 0, 1.3, -0.16),
            ...Array.from({ length: 9 }, (_, i) => [-1, 1].map(s => box(0.025, 0.28, 0.025, -R - 0.1 + (i / 8) * (R * 2 + 0.2), 1.16, s * 0.16))).flat(),
            box(0.4, 0.4, 0.4, R + 0.05, 1.2, 0, 0.03),
        ]),
    };
}

export function basinModel(W, D) {
    const t = 0.18, h = 0.8;
    return merge([
        box(W, h, t, 0, h / 2, D / 2), box(W, h, t, 0, h / 2, -D / 2),
        box(t, h, D, W / 2, h / 2, 0), box(t, h, D, -W / 2, h / 2, 0),
        box(t, h, D, 0, h / 2, 0),
        box(W, 0.05, D, 0, 0.02, 0),
    ]);
}

/**
 * A cylindrical digester: concrete shell, a shallow conical steel roof with a walkway ring and
 * railing, a stair tower; and the gas holder next to it, a membrane dome on a concrete ring.
 */
export function digesterModel() {
    const R = 1.25, H = 2.3;
    const roof = new THREE.ConeGeometry(R + 0.06, 0.45, 48, 1, true);
    roof.translate(0, H + 0.225, 0);
    const rail = new THREE.TorusGeometry(R - 0.05, 0.025, 6, 64);
    rail.rotateX(Math.PI / 2);
    rail.translate(0, H + 0.45, 0);
    const posts = Array.from({ length: 16 }, (_, k) => {
        const a = (k / 16) * Math.PI * 2;
        return cylinder(0.018, 0.018, 0.4, Math.cos(a) * (R - 0.05), H + 0.25, Math.sin(a) * (R - 0.05), 5);
    });
    const membrane = new THREE.SphereGeometry(1.15, 40, 16, 0, Math.PI * 2, 0, Math.PI * 0.42);
    membrane.scale(1, 0.75, 1);
    membrane.translate(0, 0.45, 0);
    return {
        shell: merge([cylinder(R, R, H, 0, H / 2, 0, 48), cylinder(R + 0.12, R + 0.12, 0.12, 0, H - 0.06, 0, 48)]),
        steel: merge([roof, box(0.5, 0.25, 0.5, 0, H + 0.55, 0, 0.03)]),
        rail: merge([rail, ...posts]),
        stair: merge([box(0.55, H + 0.4, 0.55, -R - 0.3, (H + 0.4) / 2, 0, 0.02), box(0.9, 0.06, 0.4, -R + 0.05, H + 0.02, 0)]),
        holderBase: merge([cylinder(1.25, 1.3, 0.45, 0, 0.225, 0, 48)]),
        membrane: merge([membrane]),
    };
}

export function controlRoomModel() {
    return {
        base: merge([box(5.4, 0.25, 4.0, 0, 0.125, 0, 0.04)]),
        body: merge([box(5.0, 3.0, 3.6, 0, 1.75, 0, 0.05)]),
        roof: merge([box(5.3, 0.15, 3.9, 0, 3.32, 0, 0.04), box(1.2, 0.5, 1.0, -1.4, 3.6, 0.6, 0.04)]),
        mast: merge([
            ...[[-0.25, -0.25], [0.25, -0.25], [0.25, 0.25], [-0.25, 0.25]].map(([x, z]) => rod(new THREE.Vector3(1.8 + x, 3.4, z), new THREE.Vector3(1.8 + x * 0.25, 9.0, z * 0.25), 0.03, 5)),
            ...[4.4, 5.6, 6.8, 8.0].map(y => {
                const s = 0.25 * (1 - ((y - 3.4) / 5.6) * 0.75);
                return merge([
                    rod(new THREE.Vector3(1.8 - s, y, -s), new THREE.Vector3(1.8 + s, y, -s), 0.015, 4),
                    rod(new THREE.Vector3(1.8 + s, y, -s), new THREE.Vector3(1.8 + s, y, s), 0.015, 4),
                    rod(new THREE.Vector3(1.8 + s, y, s), new THREE.Vector3(1.8 - s, y, s), 0.015, 4),
                    rod(new THREE.Vector3(1.8 - s, y, s), new THREE.Vector3(1.8 - s, y, -s), 0.015, 4),
                ]);
            }),
        ]),
        dish: (() => {
            const g = new THREE.SphereGeometry(0.55, 24, 12, 0, Math.PI * 2, 0, Math.PI * 0.3);
            g.rotateX(Math.PI / 2);
            g.translate(1.8, 7.2, 0.35);
            return g;
        })(),
    };
}

export function cabinetModel() {
    return {
        plinth: merge([box(1.6, 0.25, 1.0, 0, 0.125, 0, 0.03)]),
        body: merge([box(1.3, 1.9, 0.62, 0, 1.2, 0, 0.03)]),
        details: merge([
            box(0.012, 1.7, 0.01, 0, 1.2, 0.315),
            ...[0.35, 0.45, 0.55].map(y => box(0.4, 0.03, 0.01, -0.3, y + 0.25, 0.316)),
            box(0.05, 0.25, 0.04, 0.12, 1.2, 0.33),
            cylinder(0.025, 0.025, 2.0, 0.5, 3.15, 0, 6),
        ]),
    };
}
