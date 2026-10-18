import * as THREE from '../lib/three/three.module.min.js';
import { OrbitControls } from '../lib/three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from '../lib/three/addons/environments/RoomEnvironment.js';
import { EffectComposer } from '../lib/three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from '../lib/three/addons/postprocessing/RenderPass.js';
import { GTAOPass } from '../lib/three/addons/postprocessing/GTAOPass.js';
import { UnrealBloomPass } from '../lib/three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from '../lib/three/addons/postprocessing/OutputPass.js';
import { SMAAPass } from '../lib/three/addons/postprocessing/SMAAPass.js';
import { ShaderPass } from '../lib/three/addons/postprocessing/ShaderPass.js';
import { HorizontalTiltShiftShader } from '../lib/three/addons/shaders/HorizontalTiltShiftShader.js';
import { VerticalTiltShiftShader } from '../lib/three/addons/shaders/VerticalTiltShiftShader.js';
import * as M from './models.js';
import { loadKits, instance } from './kit.js';

const SCALE = 0.01;
const TANK_R = 1.9;
const TANK_H = 4.6;
const PIPE_Y = 0.3;
const RIVER_WIDTH = 7;
const RAIN_DROPS = 9000;
const TILE = 1.8;
const ROAD_W = TILE;
const BLOCK_X = 4 * TILE;
const BLOCK_Z = 3 * TILE;
const SPLASHES = 700;
const FILM = new URLSearchParams(location.search).has('film');

const SKY = { zenith: 0x4f9fe3, horizon: 0xe4f2fb, fog: 0xd9ecf8, stormFog: 0x8f9eac };
const WIDTH = { 'Main River': 5.5, 'Park Creek': 2.2 };
const COLOR = {
    fog: SKY.fog,
    flow: 0x39b8ff,
    dirty: 0x9a6a2e,
    gateOk: 0x2f6f9f,
    gateBad: 0xff4d4f,
    linkUp: 0x0a84d6,
    linkIdle: 0x5b6878,
    linkBad: 0xff4d4f,
    alarm: { high: 0xff3b3b, medium: 0xff9b2f, low: 0xffd84a },
    select: 0x7fe3ff,
};

const ASSETS = 'live/assets/';

/** Water normals for the water surfaces and the Kenney low-poly kits (CC0). */
async function loadAssets() {
    const water = await new THREE.TextureLoader().loadAsync(ASSETS + 'waternormals.jpg').then(t => {
        t.wrapS = t.wrapT = THREE.RepeatWrapping;
        return t;
    }).catch(() => null);
    const kits = await loadKits().catch(() => ({}));
    return { water, kits };
}

/** Flat-shaded low-poly material. */
function flat(color, params = {}) {
    return new THREE.MeshStandardMaterial({ color, roughness: 0.85, metalness: 0, flatShading: true, ...params });
}

function pointInPolygon(x, z, poly) {
    let inside = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
        const xi = poly[i].x, zi = poly[i].z, xj = poly[j].x, zj = poly[j].z;
        if ((zi > z) !== (zj > z) && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) inside = !inside;
    }
    return inside;
}

function distanceToPolygonEdge(q, poly) {
    let d = Infinity;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) d = Math.min(d, distanceToSegment(q, poly[j], poly[i]));
    return d;
}

export async function create(host, network, dotnet) {
    const assets = await loadAssets();
    const scene = new LiveScene(host, network, dotnet, assets);
    return {
        update: frame => scene.update(frame),
        focus: id => scene.focus(id),
        overview: () => scene.overview(),
        dispose: () => scene.dispose(),
    };
}

/** Small deterministic generator, so the town looks the same on every load. */
function random(seed) {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

function plainName(id) {
    let m = /^T(\d+)$/.exec(id);
    if (m) return `Tank ${m[1]}`;
    m = /^CSO(\d+)$/.exec(id);
    if (m) return `Overflow ${m[1]}`;
    m = /^V(\d+)$/.exec(id);
    if (m) return `Gate ${m[1]}`;
    return id;
}

function distanceToSegment(p, a, b) {
    const abx = b.x - a.x, abz = b.z - a.z;
    const len2 = abx * abx + abz * abz || 1e-9;
    const t = Math.max(0, Math.min(1, ((p.x - a.x) * abx + (p.z - a.z) * abz) / len2));
    return Math.hypot(a.x + abx * t - p.x, a.z + abz * t - p.z);
}

/** A cylinder from a to b (scene units), its texture v running from a to b. */
function segment(a, b, radius, material, radial = 16) {
    const dir = new THREE.Vector3().subVectors(b, a);
    const length = dir.length();
    const geometry = new THREE.CylinderGeometry(radius, radius, length, radial, 1, true);
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.copy(a).addScaledVector(dir, 0.5);
    mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.normalize());
    return { mesh, length };
}

function stripeTexture() {
    const canvas = document.createElement('canvas');
    canvas.width = 4;
    canvas.height = 64;
    const g = canvas.getContext('2d');
    const gradient = g.createLinearGradient(0, 0, 0, 64);
    gradient.addColorStop(0, '#000');
    gradient.addColorStop(0.55, '#000');
    gradient.addColorStop(0.8, '#fff');
    gradient.addColorStop(1, '#000');
    g.fillStyle = gradient;
    g.fillRect(0, 0, 4, 64);
    const texture = new THREE.CanvasTexture(canvas);
    texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
    return texture;
}

function curtainTexture() {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 128;
    const g = canvas.getContext('2d');
    g.fillStyle = '#000';
    g.fillRect(0, 0, 128, 128);
    let a = 9;
    const rand = () => ((a = (a * 16807) % 2147483647) / 2147483647);
    for (let i = 0; i < 220; i++) {
        const x = rand() * 128, w = 0.6 + rand() * 1.8, y = rand() * 128, h = 20 + rand() * 90;
        const grad = g.createLinearGradient(0, y, 0, y + h);
        grad.addColorStop(0, 'rgba(255,255,255,0)');
        grad.addColorStop(0.5, `rgba(255,255,255,${0.35 + rand() * 0.65})`);
        grad.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = grad;
        g.fillRect(x, y, w, h);
        g.fillRect(x, y - 128, w, h);
    }
    const t = new THREE.CanvasTexture(canvas);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    return t;
}

const RIVER_SPOTS = 12;

const WAVES =  `
float wave(vec2 p, float t) {
    return sin(p.x * 0.9 + t * 1.3) * 0.5 + cos(p.y * 1.1 - t * 1.05) * 0.5 + sin((p.x + p.y) * 0.6 + t * 0.8) * 0.35;
}`;

const riverVertex =  `
uniform float uTime;
uniform float uAmp;
varying vec2 vUv;
varying vec3 vWorld;
${WAVES}
void main() {
    vUv = uv;
    vec4 world = modelMatrix * vec4(position, 1.0);
    world.y += wave(world.xz, uTime) * uAmp;
    vWorld = world.xyz;
    gl_Position = projectionMatrix * viewMatrix * world;
}`;

const riverFragment =  `
uniform float uTime;
uniform vec2 uSpots[${RIVER_SPOTS}];
uniform vec2 uActive[${RIVER_SPOTS}];
uniform vec3 uDeep;
uniform vec3 uSky;
uniform vec3 uDirt;
uniform vec3 uFog;
uniform vec3 uMoonDir;
uniform float uFogDensity;
varying vec2 vUv;
varying vec3 vWorld;
void main() {
    // One normal per triangle: the facets of low-poly water.
    vec3 n = normalize(cross(dFdx(vWorld), dFdy(vWorld)));
    if (n.y < 0.0) n = -n;
    vec3 v = normalize(cameraPosition - vWorld);
    float lambert = max(dot(n, uMoonDir), 0.0);
    float bank = 1.0 - abs(vUv.y - 0.5) * 2.0;
    vec3 col = mix(uSky, uDeep, 0.55 + bank * 0.35) * (0.6 + 0.55 * lambert);
    float spec = pow(max(dot(reflect(-uMoonDir, n), v), 0.0), 24.0);
    col += vec3(1.0) * spec * 0.35;
    float dirt = 0.0;
    for (int i = 0; i < ${RIVER_SPOTS}; i++) {
        float amount = uSpots[i].y;
        if (amount <= 0.0) continue;
        float d = vUv.x - uSpots[i].x;
        float len = 0.006 + 0.09 * amount;
        float along = d >= 0.0 ? exp(-d / len) : exp(d / 0.003);
        float width = 0.3 + 0.7 * amount;
        float across = 1.0 - smoothstep(width * 0.6, width, abs(vUv.y - 0.5) * 2.0 * 0.8);
        float pulse = uActive[i].x * (0.15 + 0.1 * sin(uTime * 6.0));
        dirt = max(dirt, (amount * 0.9 + pulse) * along * across);
    }
    dirt = clamp(dirt, 0.0, 0.85);
    col = mix(col, uDirt * (0.75 + 0.35 * lambert), dirt);
    float fogFactor = 1.0 - exp(-uFogDensity * uFogDensity * dot(vWorld - cameraPosition, vWorld - cameraPosition));
    gl_FragColor = vec4(mix(col, uFog, clamp(fogFactor, 0.0, 1.0)), 1.0);
    #include <colorspace_fragment>
}`;

const skyVertex =  `
varying vec3 vDir;
void main() {
    vDir = normalize(position);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}`;

const skyFragment =  `
uniform vec3 uZenith;
uniform vec3 uHorizon;
uniform float uStorm;
varying vec3 vDir;
void main() {
    float h = clamp(vDir.y, 0.0, 1.0);
    vec3 col = mix(uHorizon, uZenith, pow(h, 0.55));
    col = mix(col, vec3(0.42, 0.47, 0.53), uStorm * 0.85);
    gl_FragColor = vec4(col, 1.0);
    #include <colorspace_fragment>
}`;

class LiveScene {
    constructor(host, network, dotnet, assets = {}) {
        this.assets = assets;
        this.host = host;
        this.network = network;
        this.dotnet = dotnet;
        this.rand = random(20000530);
        this.clock = new THREE.Clock();
        this.pickables = [];
        this.labels = [];
        this.pipes = [];
        this.channels = new Map();
        this.tanks = new Map();
        this.gates = new Map();
        this.weirs = new Map();
        this.frame = null;
        this.flash = 0;
        this.lastInteraction = performance.now();
        this.selected = null;
        this.tween = null;
        this.disposed = false;
        this.tex = M.makeTextures(this.rand);
        this.mat = M.makeMaterials(this.tex);
        if (assets.water) this.tex.waterNormals = assets.water;
        this.kits = assets.kits ?? {};
        Object.assign(this.mat, {
            concrete: flat(0xd9dde2, { side: THREE.DoubleSide }),
            concreteTank: flat(0xe8ebef, { side: THREE.DoubleSide }),
            concreteDark: flat(0xa9b0b8),
            band: flat(0xf39a2b, { side: THREE.DoubleSide }),
            steel: flat(0xb8c2cc, { metalness: 0.3, roughness: 0.5 }),
            darkSteel: flat(0x56606b, { metalness: 0.2 }),
            paint: flat(0x2f86c9),
            yellow: flat(0xf2b632),
            castIron: flat(0x3d434a),
        });
        this.glow = M.glowTexture();
        this.stripes = stripeTexture();
        this.curtain = curtainTexture();

        this.setUpRenderer();
        this.layOut();
        this.buildSky();
        this.buildGround();
        this.buildRiver();
        this.buildPipes();
        this.buildTanks();
        this.buildGates();
        this.buildWeirs();
        this.buildChannels();
        this.buildPlant();
        this.buildControl();
        this.buildStreets();
        this.buildTown();
        this.buildRain();
        this.buildSplashes();
        this.buildSelection();
        this.overview(true);

        this.resizeObserver = new ResizeObserver(() => this.resize());
        this.resizeObserver.observe(host);
        this.resize();
        this.bindPointer();
        this.time = 0;
        this.manual = false;
        this.loop = () => {
            if (this.disposed) return;
            this.raf = requestAnimationFrame(this.loop);
            if (!this.manual) this.tick(Math.min(this.clock.getDelta(), 0.1));
        };
        this.loop();
        if (FILM) {
            window.__liveFilm = {
                manual: on => { this.manual = !!on; this.clock.getDelta(); },
                advance: dt => this.tick(dt),
                opening: () => this.openingShot(),
                topDown: () => {
                    const b = this.bounds, t = new THREE.Vector3((b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2);
                    const half = Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2));
                    const h = Math.max((b.maxZ - b.minZ) / 2 / half, (b.maxX - b.minX) / 2 / (half * this.camera.aspect)) * 1.12;
                    this.tween = null;
                    this.filmHold = true;
                    this.controls.target.copy(t);
                    this.camera.position.copy(t).add(new THREE.Vector3(0, h, 0.001));
                    this.controls.update(0);
                },
                reveal: () => { this.filmHold = false; this.overview(); },
                clean: on => document.body.classList.toggle('film-clean', !!on),
                dry: on => {
                    this.filmDry = !!on;
                    if (!on) return;
                    this.frame = null;
                    for (const tank of this.tanks.values()) {
                        Object.assign(tank, { target: 0, fill: 0, sensorFill: null, overflowing: false, alarm: null });
                        tank.spill.level = 0;
                    }
                    for (const gate of this.gates.values()) gate.alarm = null;
                    for (const ch of this.channels.values()) Object.assign(ch, { amount: 0, active: 0 });
                },
                project: () => {
                    const r = this.renderer.domElement.getBoundingClientRect();
                    return this.gauges.map(g => {
                        const v = g.at.clone().project(this.camera);
                        return { id: g.id, x: (v.x + 1) / 2 * r.width, y: (1 - v.y) / 2 * r.height };
                    });
                },
                look: (target, position) => this.flyTo(new THREE.Vector3(...target), new THREE.Vector3(...position)),
                debug: this,
                nodes: () => [...this.pos].map(([id, q]) => ({ id, at: q.toArray(), water: this.nearestWater(q).distance })),
                places: () => this.places.map(pl => ({ name: pl.name, at: pl.at.toArray() }))
                    .concat(this.catchments.map(c => ({ name: c.name, at: c.poly.reduce((a, q) => a.add(q), new THREE.Vector3()).divideScalar(c.poly.length).toArray() }))),
            };
        }
    }

    setUpRenderer() {
        const renderer = new THREE.WebGLRenderer({ antialias: false, powerPreference: 'high-performance' });
        renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5));
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 0.95;
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        renderer.domElement.className = 'live-gl';
        this.host.appendChild(renderer.domElement);
        this.renderer = renderer;

        this.labelLayer = document.createElement('div');
        this.labelLayer.className = 'live-labels';
        this.host.appendChild(this.labelLayer);

        const scene = new THREE.Scene();
        scene.fog = new THREE.FogExp2(SKY.fog, 0.0016);
        const pmrem = new THREE.PMREMGenerator(renderer);
        scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
        scene.environmentIntensity = 0.3;
        pmrem.dispose();
        this.scene = scene;

        this.camera = new THREE.PerspectiveCamera(38, 1, 0.5, 1400);
        this.controls = new OrbitControls(this.camera, renderer.domElement);
        this.controls.enableDamping = true;
        this.controls.dampingFactor = 0.08;
        this.controls.maxPolarAngle = Math.PI * 0.46;
        this.controls.minDistance = 8;
        this.controls.maxDistance = 190;
        this.controls.autoRotateSpeed = 0.35;
        this.controls.addEventListener('start', () => {
            this.lastInteraction = performance.now();
            this.tween = null;
        });

        this.hemi = new THREE.HemisphereLight(0xf2f6ff, 0x7a9a55, 0.95);
        scene.add(this.hemi);
        this.moonDir = new THREE.Vector3(-35, 80, 45).normalize();
        const sun = new THREE.DirectionalLight(0xfff0d6, 2.9);
        sun.position.copy(this.moonDir).multiplyScalar(140);
        sun.castShadow = true;
        sun.shadow.mapSize.set(4096, 4096);
        const sc = sun.shadow.camera;
        sc.left = -110; sc.right = 110; sc.top = 80; sc.bottom = -80; sc.near = 10; sc.far = 320;
        sun.shadow.bias = -0.0004;
        sun.shadow.normalBias = 0.04;
        sun.shadow.radius = 2.5;
        scene.add(sun);
        this.sun = sun;

        this.composer = new EffectComposer(renderer);
        this.composer.addPass(new RenderPass(scene, this.camera));
        this.gtao = new GTAOPass(scene, this.camera, 256, 256);
        this.gtao.updateGtaoMaterial({ radius: 1.2, distanceExponent: 1.5, thickness: 2, scale: 1.2, samples: 12 });
        this.gtao.blendIntensity = 0.7;
        const hideForAo = this.gtao._overrideVisibility.bind(this.gtao);
        this.gtao._overrideVisibility = () => {
            hideForAo();
            this.scene.traverse(o => {
                const m = o.material;
                if (o.visible && (o.isSprite || (m && !Array.isArray(m) && m.transparent))) {
                    o.visible = false;
                    this.gtao._visibilityCache.push(o);
                }
            });
        };
        this.composer.addPass(this.gtao);
        this.bloom = new UnrealBloomPass(new THREE.Vector2(256, 256), 0.18, 0.4, 0.92);
        this.composer.addPass(this.bloom);
        if (FILM) {
            this.tiltH = new ShaderPass(HorizontalTiltShiftShader);
            this.tiltV = new ShaderPass(VerticalTiltShiftShader);
            for (const pass of [this.tiltH, this.tiltV]) {
                pass.uniforms.r.value = 0.5;
                this.composer.addPass(pass);
            }
        }
        this.composer.addPass(new OutputPass());
        this.smaa = new SMAAPass(256, 256);
        this.composer.addPass(this.smaa);
    }

    layOut() {
        const net = this.network;
        const xs = net.nodes.map(n => n.x), ys = net.nodes.map(n => n.y);
        const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
        const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
        this.pos = new Map(net.nodes.map(n => [n.id, new THREE.Vector3((n.x - cx) * SCALE, 0, -(n.y - cy) * SCALE)]));
        const ps = [...this.pos.values()];
        this.bounds = {
            minX: Math.min(...ps.map(p => p.x)), maxX: Math.max(...ps.map(p => p.x)),
            minZ: Math.min(...ps.map(p => p.z)), maxZ: Math.max(...ps.map(p => p.z)),
        };
        this.tankSet = new Set(net.tanks);
        for (const id of net.overflowPoints) {
            if (this.tankSet.has(id) || !this.pos.has(id)) continue;
            const p = this.pos.get(id);
            for (const tank of net.tanks) {
                const c = this.pos.get(tank);
                if (!c) continue;
                const away = p.clone().sub(c).setY(0);
                const clear = TANK_R + 3.6;
                if (away.length() < clear) p.copy(c).addScaledVector(away.lengthSq() > 1e-6 ? away.normalize() : new THREE.Vector3(1, 0, 0), clear);
            }
        }
        const toScene = ([x, y]) => new THREE.Vector3((x - cx) * SCALE, 0, -(y - cy) * SCALE);
        const map = net.map ?? {};
        this.catchments = (map.catchments ?? []).filter(c => c.polygon?.length >= 3).map(c => ({
            ...c, poly: c.polygon.map(toScene), tint: map.tints?.[c.id] ?? '#e6eef2',
        }));
        this.gauges = (map.gauges ?? []).map(g => ({ id: g.id, at: toScene([g.x, g.y]) }));
        this.areas = (map.areas ?? []).map(a => ({ name: a.name, poly: a.polygon.map(toScene) }));
        this.places = (map.places ?? []).map(pl => ({ name: pl.name, at: toScene([pl.x, pl.y]) }));
        let waters = (map.waters ?? []).filter(w => w.line?.length >= 2).map(w => ({ name: w.name, line: w.line.map(toScene) }));
        if (!waters.length) {
            const z = this.bounds.maxZ + 8;
            waters = [{ name: 'River', line: [new THREE.Vector3(this.bounds.minX - 60, 0, z), new THREE.Vector3(this.bounds.maxX + 60, 0, z)] }];
            this.places.push({ name: 'River', at: new THREE.Vector3((this.bounds.minX + this.bounds.maxX) / 2, 0, z) });
        }
        this.waters = waters.map(w => this.waterPath(w));
        for (const id of [...net.tanks, ...net.overflowPoints.filter(o => !this.tankSet.has(o))]) {
            const p = this.pos.get(id);
            if (!p) continue;
            const size = this.tankSet.has(id) ? TANK_R + 1.0 : 2.0;
            for (let pass = 0; pass < 12; pass++) {
                const push = new THREE.Vector3();
                for (const w of this.waters) {
                    const near = this.nearestWater(p, w);
                    const clear = size - near.distance;
                    if (clear <= 0) continue;
                    const away = p.clone().sub(near.centre).setY(0);
                    push.addScaledVector(away.lengthSq() > 1e-6 ? away.normalize() : near.normal, clear + 0.2);
                }
                if (push.lengthSq() < 1e-6) break;
                p.add(push);
            }
        }
        const structures = new Set([...net.tanks, ...net.overflowPoints, net.wwtp]);
        const moved = new Set();
        const neighbours = id => net.links.flatMap(l => l.from === id ? [l.to] : l.to === id ? [l.from] : []).map(n => this.pos.get(n)).filter(Boolean);
        for (let pass = 0; pass < 2; pass++) {
            for (const [id, p] of this.pos) {
                if (structures.has(id)) continue;
                const near = this.nearestWater(p);
                if (near.distance > 1.2) continue;
                const pull = neighbours(id).reduce((a, q) => a + q.clone().sub(near.centre).dot(near.normal), 0);
                const own = p.clone().sub(near.centre).dot(near.normal);
                const side = Math.sign(Math.abs(pull) > 0.5 ? pull : own) || 1;
                p.copy(near.centre).addScaledVector(near.normal, side * (near.water.width / 2 + 1.3));
                moved.add(id);
            }
        }
        for (let pass = 0; pass < 4; pass++) {
            for (const a of moved) {
                for (const b of moved) {
                    const p = this.pos.get(a), q = this.pos.get(b);
                    if (a >= b || p.distanceTo(q) > 1.6) continue;
                    const near = this.nearestWater(p);
                    const along = new THREE.Vector3(near.normal.z, 0, -near.normal.x);
                    const d = q.clone().sub(p).dot(along) >= 0 ? 1 : -1;
                    const gap = (1.7 - Math.abs(q.clone().sub(p).dot(along))) / 2;
                    p.addScaledVector(along, -d * gap);
                    q.addScaledVector(along, d * gap);
                }
            }
        }
        const outfall = this.pos.get(net.wwtp);
        for (let pass = 0; outfall && pass < 6; pass++) {
            const centre = outfall.clone().add(new THREE.Vector3(9.6, 0, 0.4));
            const near = this.nearestWater(centre);
            const clear = 9.5 - near.distance;
            if (clear <= 0) break;
            const away = centre.sub(near.centre).setY(0);
            outfall.addScaledVector(away.lengthSq() > 1e-6 ? away.normalize() : new THREE.Vector3(0, 0, -1), clear + 0.3);
        }
        for (const c of this.catchments) for (const q of c.poly) this.extend(q);
        for (const w of this.waters) for (const q of w.pts) if (Math.abs(q.x) < 90) this.extend(q);
    }

    extend(q) {
        const b = this.bounds;
        b.minX = Math.min(b.minX, q.x); b.maxX = Math.max(b.maxX, q.x);
        b.minZ = Math.min(b.minZ, q.z); b.maxZ = Math.max(b.maxZ, q.z);
    }

    /** A smooth path along a water's line: samples, arc length, width. */
    waterPath(w) {
        const curve = new THREE.CatmullRomCurve3(w.line, false, 'centripetal');
        const n = Math.max(40, Math.round(curve.getLength() * 2));
        const pts = curve.getSpacedPoints(n);
        const cum = [0];
        for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + pts[i].distanceTo(pts[i - 1]));
        return { name: w.name, pts, cum, total: cum[cum.length - 1], width: WIDTH[w.name] ?? 5, spots: [] };
    }

    /** The nearest point on any water's bank: the point, the water, and where along it (0..1). */
    nearestWater(q, only = null) {
        let best = null;
        for (const w of only ? [only] : this.waters) {
            for (let i = 1; i < w.pts.length; i++) {
                const a = w.pts[i - 1], b = w.pts[i];
                const ab = b.clone().sub(a);
                const t = Math.max(0, Math.min(1, q.clone().sub(a).dot(ab) / ab.lengthSq()));
                const c = a.clone().addScaledVector(ab, t);
                const d = c.distanceTo(q.clone().setY(0));
                if (!best || d < best.d) best = { d, centre: c, water: w, ab, u: (w.cum[i - 1] + t * (w.cum[i] - w.cum[i - 1])) / w.total };
            }
        }
        const towards = q.clone().setY(0).sub(best.centre).setY(0);
        const bank = best.centre.clone().addScaledVector(towards.lengthSq() > 1e-6 ? towards.normalize() : new THREE.Vector3(0, 0, -1), best.water.width / 2);
        const normal = new THREE.Vector3(-best.ab.z, 0, best.ab.x).normalize();
        return { point: bank, centre: best.centre, normal, water: best.water, u: best.u, distance: best.d - best.water.width / 2 };
    }

    label(id, anchor, height, cls, pick = id) {
        const el = document.createElement('div');
        el.className = `l3d ${cls}`;
        el.innerHTML = `<span class="l3d-name">${plainName(id)}</span><span class="l3d-value"></span>`;
        el.addEventListener('click', e => { e.stopPropagation(); this.pick(pick); });
        this.labelLayer.appendChild(el);
        const item = { id, pick, el, value: el.querySelector('.l3d-value'), anchor: anchor.clone().setY(anchor.y + height), text: '', cls: '' };
        this.labels.push(item);
        return item;
    }

    setLabel(item, text, state = '') {
        if (item.text !== text) {
            item.value.textContent = text;
            item.text = text;
        }
        if (item.cls !== state) {
            item.el.dataset.state = state;
            item.cls = state;
        }
    }

    pickable(mesh, id) {
        mesh.userData.pick = id;
        this.pickables.push(mesh);
    }

    add(geometry, material, { shadow = true, receive = true, parent = this.scene } = {}) {
        const mesh = new THREE.Mesh(geometry, material);
        mesh.castShadow = shadow;
        mesh.receiveShadow = receive;
        parent.add(mesh);
        return mesh;
    }

    buildSky() {
        this.skyUniforms = {
            uZenith: { value: new THREE.Color(SKY.zenith) },
            uHorizon: { value: new THREE.Color(SKY.horizon) },
            uStorm: { value: 0 },
        };
        const sky = new THREE.Mesh(new THREE.SphereGeometry(600, 32, 16), new THREE.ShaderMaterial({
            uniforms: this.skyUniforms, vertexShader: skyVertex, fragmentShader: skyFragment, side: THREE.BackSide, depthWrite: false, fog: false,
        }));
        this.scene.add(sky);
        const puff = new THREE.IcosahedronGeometry(1, 1);
        const cloudMat = flat(0xffffff, { roughness: 1, transparent: true, opacity: 0.92 });
        this.cloudMat = cloudMat;
        this.clouds = new THREE.Group();
        const b = this.bounds;
        for (let i = 0; i < 26; i++) {
            const cloud = new THREE.Group();
            for (let k = 0; k < 5; k++) {
                const m = new THREE.Mesh(puff, cloudMat);
                const s = 2.2 + this.rand() * 2.6;
                m.scale.set(s * 1.4, s * 0.8, s);
                m.position.set((k - 2) * 2.4 + this.rand(), this.rand() * 1.2, this.rand() * 2 - 1);
                cloud.add(m);
            }
            const a = this.rand() * Math.PI * 2, r = 150 + this.rand() * 80;
            cloud.position.set((b.minX + b.maxX) / 2 + Math.cos(a) * r, 26 + this.rand() * 18, (b.minZ + b.maxZ) / 2 + Math.sin(a) * r);
            cloud.scale.setScalar(2.2 + this.rand() * 1.6);
            cloud.userData.speed = 0.4 + this.rand() * 0.6;
            this.clouds.add(cloud);
        }
        this.scene.add(this.clouds);
    }

    buildGround() {
        const ground = new THREE.Mesh(new THREE.PlaneGeometry(900, 900), flat(0x8fc760, { roughness: 1 }));
        ground.rotation.x = -Math.PI / 2;
        ground.receiveShadow = true;
        this.scene.add(ground);
        for (const c of this.catchments) {
            const shape = new THREE.Shape(c.poly.map(q => new THREE.Vector2(q.x, -q.z)));
            const geometry = new THREE.ShapeGeometry(shape);
            geometry.rotateX(-Math.PI / 2);
            const tint = new THREE.Color(c.tint);
            const hsl = {};
            tint.getHSL(hsl);
            tint.setHSL(hsl.h, Math.min(1, hsl.s * 1.1 + 0.15), Math.max(0.45, hsl.l - 0.14));
            const fill = new THREE.Color(0x8fc760).lerp(tint, 0.4);
            const patch = new THREE.Mesh(geometry, flat(fill, { roughness: 1 }));
            patch.position.y = 0.012;
            patch.receiveShadow = true;
            this.scene.add(patch);
            const edge = c.poly.concat([c.poly[0]]).map(q => q.clone().setY(0.03));
            this.scene.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(edge), new THREE.LineBasicMaterial({ color: tint.clone().multiplyScalar(0.7) })));
        }
        for (const a of this.areas) {
            const shape = new THREE.Shape(a.poly.map(q => new THREE.Vector2(q.x, -q.z)));
            const geometry = new THREE.ShapeGeometry(shape);
            geometry.rotateX(-Math.PI / 2);
            const patch = new THREE.Mesh(geometry, flat(0x6fbf4a, { roughness: 1 }));
            patch.position.y = 0.014;
            patch.receiveShadow = true;
            this.scene.add(patch);
        }
    }

    /**
     * Both edges of a strip `half` either side of a water's line. On the inside of a bend tighter
     * than `half` the edge would run backwards and fold the strip over; there it waits instead.
     */
    edges(w, half) {
        const n = w.pts.length, left = [], right = [], dirs = [], sides = [];
        for (let i = 0; i < n; i++) {
            const a = w.pts[Math.max(0, i - 1)], b = w.pts[Math.min(n - 1, i + 1)];
            const dir = b.clone().sub(a).setY(0).normalize();
            const side = new THREE.Vector3(-dir.z, 0, dir.x);
            dirs.push(dir);
            sides.push(side);
            for (const [list, sign] of [[left, -1], [right, 1]]) {
                const q = w.pts[i].clone().addScaledVector(side, sign * half);
                const prev = list[i - 1];
                list.push(prev && q.clone().sub(prev).dot(dir) <= 0.02 ? prev.clone() : q);
            }
        }
        return { left, right, dirs, sides };
    }

    buildRiver() {
        for (const w of this.waters) {
            const positions = [], uvs = [], index = [];
            const n = w.pts.length, across = w.width > 3 ? 5 : 3;
            const { left, right, dirs, sides } = this.edges(w, w.width / 2);
            for (let i = 0; i < n; i++) {
                const dir = dirs[i], side = sides[i];
                for (let k = 0; k <= across; k++) {
                    const f = k / across;
                    const edge = k === 0 || k === across;
                    const jitter = edge || i === 0 || i === n - 1 ? 0 : (this.rand() - 0.5) * 0.45;
                    const q = left[i].clone().lerp(right[i], f).addScaledVector(side, jitter * w.width / across).addScaledVector(dir, edge ? 0 : (this.rand() - 0.5) * 0.4);
                    positions.push(q.x, 0, q.z);
                    uvs.push(w.cum[i] / w.total, f);
                }
                if (i < n - 1) {
                    for (let k = 0; k < across; k++) {
                        const p0 = i * (across + 1) + k, p1 = p0 + 1, q0 = p0 + across + 1, q1 = q0 + 1;
                        if ((i + k) % 2) index.push(p0, q0, p1, p1, q0, q1);
                        else index.push(p0, q0, q1, p0, q1, p1);
                    }
                }
            }
            const geometry = new THREE.BufferGeometry();
            geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
            geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
            geometry.setIndex(index);
            geometry.computeVertexNormals();
            const uniforms = {
                uTime: { value: 0 },
                uAmp: { value: w.width > 3 ? 0.2 : 0.1 },
                uSpots: { value: Array.from({ length: RIVER_SPOTS }, () => new THREE.Vector2(0, 0)) },
                uActive: { value: Array.from({ length: RIVER_SPOTS }, () => new THREE.Vector2(0, 0)) },
                uDeep: { value: new THREE.Color(0x2a86c9) },
                uSky: { value: new THREE.Color(0x7cc6ee) },
                uDirt: { value: new THREE.Color(0x7d8c72) },
                uFog: { value: new THREE.Color(SKY.fog) },
                uMoonDir: { value: this.moonDir },
                uFogDensity: { value: 0.0016 },
            };
            const mesh = new THREE.Mesh(geometry, new THREE.ShaderMaterial({ uniforms, vertexShader: riverVertex, fragmentShader: riverFragment, side: THREE.DoubleSide }));
            mesh.position.y = 0.34;
            this.scene.add(mesh);
            this.pickable(mesh, 'river');
            w.uniforms = uniforms;
            const bank = [], bankIndex = [];
            const shore = this.edges(w, w.width / 2 + 0.8);
            for (let i = 0; i < n; i++) {
                const l = shore.left[i], r = shore.right[i];
                bank.push(l.x, 0, l.z, r.x, 0, r.z);
                if (i < n - 1) bankIndex.push(i * 2, i * 2 + 2, i * 2 + 1, i * 2 + 1, i * 2 + 2, i * 2 + 3);
            }
            const bankGeometry = new THREE.BufferGeometry();
            bankGeometry.setAttribute('position', new THREE.Float32BufferAttribute(bank, 3));
            bankGeometry.setIndex(bankIndex);
            bankGeometry.computeVertexNormals();
            const sand = new THREE.Mesh(bankGeometry, flat(0xe8d49a, { roughness: 1, side: THREE.DoubleSide }));
            sand.position.y = 0.025;
            sand.receiveShadow = true;
            this.scene.add(sand);
        }
        this.buildPlaces();
    }

    /** A disc of jittered triangles, flat in xz, for low-poly water in tanks and basins. */
    waterDisc(radius, rings = 6, sectors = 24) {
        const g = new THREE.RingGeometry(0, radius, sectors, rings);
        g.rotateX(-Math.PI / 2);
        const base = radius / rings * 0.35;
        return this.wavy(g, (x, z) => Math.min(base, 0.3 * 2 * Math.PI * Math.hypot(x, z) / sectors), radius * 0.98);
    }

    waterPlane(w, d, cols, rows) {
        const g = new THREE.PlaneGeometry(w, d, cols, rows);
        g.rotateX(-Math.PI / 2);
        return this.wavy(g, Math.min(w / cols, d / rows) * 0.35, null, [w / 2, d / 2]);
    }

    /** Jitter the inner vertices once, then let them bob each frame (animateWater). */
    wavy(g, jitter, maxRadius, box = null) {
        const pos = g.attributes.position;
        for (let i = 0; i < pos.count; i++) {
            const x = pos.getX(i), z = pos.getZ(i);
            const inner = box ? Math.abs(x) < box[0] - 1e-3 && Math.abs(z) < box[1] - 1e-3 : Math.hypot(x, z) < maxRadius;
            const j = typeof jitter === 'function' ? jitter(x, z) : jitter;
            const h = (k) => { const v = Math.sin(Math.round(x * 1000) * 12.9898 + Math.round(z * 1000) * 78.233 + k) * 43758.5453; return v - Math.floor(v); };
            if (inner) pos.setXYZ(i, x + (h(0) - 0.5) * j * 2, 0, z + (h(1.7) - 0.5) * j * 2);
        }
        this.waterSurfaces ??= [];
        this.waterSurfaces.push({ g, base: Float32Array.from(pos.array) });
        g.computeVertexNormals();
        return g;
    }

    animateWater(t) {
        for (const { g, base } of this.waterSurfaces ?? []) {
            const a = g.attributes.position.array;
            for (let i = 0; i < a.length; i += 3) {
                const x = base[i], z = base[i + 2];
                a[i + 1] = (Math.sin(x * 1.9 + t * 1.6) * 0.5 + Math.cos(z * 2.2 - t * 1.25) * 0.5) * 0.08;
            }
            g.attributes.position.needsUpdate = true;
        }
    }

    /** Place names, rain gauges: the labels and symbols of the paper's figure. */
    buildPlaces() {
        for (const pl of this.places) {
            const kind = /river/i.test(pl.name) ? 'water' : /creek/i.test(pl.name) ? 'creek' : 'area';
            this.label(pl.name, pl.at, 0.4, `place ${kind}`, null);
        }
        const mast = M.merge([M.cylinder(0.05, 0.06, 2.2, 0, 1.1, 0, 8), M.cylinder(0.32, 0.22, 0.42, 0, 2.35, 0, 12), M.box(0.5, 0.35, 0.35, 0, 0.6, 0.22, 0.03)]);
        const white = flat(0xf4f6f8), base = flat(0x7b8794);
        for (const g of this.gauges) {
            const group = new THREE.Group();
            group.position.copy(g.at);
            this.add(mast, white, { parent: group });
            this.add(M.box(1.2, 0.12, 1.2, 0, 0.06, 0), base, { parent: group });
            this.scene.add(group);
            this.label(`${g.id} rain gauge`, g.at, 3.2, 'gauge', null);
        }
    }

    outletStart(tank, toward) {
        return tank.clone().add(toward.clone().sub(tank).setY(0).normalize().multiplyScalar(TANK_R + 0.35));
    }

    buildPipes() {
        const net = this.network;
        const casing = flat(0x8d99a6, { roughness: 0.6 });
        const flange = new THREE.MeshStandardMaterial({ color: 0x5c6670, roughness: 0.45, metalness: 0.8 });
        const saddles = new Map();
        const saddleFor = r => saddles.get(r) ?? saddles.set(r, new THREE.BoxGeometry(r * 2.3, PIPE_Y - r * 0.3, 0.22)).get(r);
        const joints = new Set();
        for (const link of net.links) {
            const a0 = this.pos.get(link.from), b0 = this.pos.get(link.to);
            if (!a0 || !b0) continue;
            let a = a0.clone().setY(PIPE_Y), b = b0.clone().setY(PIPE_Y);
            const isOutlet = link.kind === 'orifice' && this.tankSet.has(link.from);
            if (this.tankSet.has(link.from)) a = this.outletStart(a0, b0).setY(PIPE_Y);
            if (this.tankSet.has(link.to)) b = this.outletStart(b0, a0).setY(PIPE_Y);
            const radius = isOutlet ? 0.24 : 0.2;
            const { mesh, length } = segment(a, b, radius, casing);
            mesh.castShadow = mesh.receiveShadow = true;
            this.scene.add(mesh);
            const dir = b.clone().sub(a).normalize();
            const quat = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
            const fg = new THREE.CylinderGeometry(radius * 1.35, radius * 1.35, 0.07, 18);
            const pieces = Math.max(1, Math.round(length / 3));
            for (let i = 0; i <= pieces; i++) {
                const f = new THREE.Mesh(fg, flange);
                f.position.copy(a).addScaledVector(dir, (i / pieces) * length);
                f.quaternion.copy(quat);
                f.castShadow = true;
                this.scene.add(f);
            }
            for (let d = 1.2; d < length - 0.6; d += 3.2) {
                const s = new THREE.Mesh(saddleFor(radius), this.mat.concreteDark);
                s.position.copy(a).addScaledVector(dir, d).setY((PIPE_Y - radius * 0.3) / 2);
                s.rotation.y = Math.atan2(dir.x, dir.z);
                s.castShadow = s.receiveShadow = true;
                this.scene.add(s);
            }

            const texture = this.stripes.clone();
            texture.needsUpdate = true;
            const flowMaterial = new THREE.MeshBasicMaterial({
                color: 0x1d8fe0, alphaMap: texture, transparent: true, opacity: 0, depthWrite: false,
            });
            const flow = segment(a, b, radius * 1.08, flowMaterial);
            texture.repeat.set(1, Math.max(1, flow.length / 1.4));
            this.scene.add(flow.mesh);
            this.pipes.push({ link, texture, material: flowMaterial, isOutlet, level: 0 });
            if (!this.tankSet.has(link.from)) joints.add(link.from);
            if (!this.tankSet.has(link.to)) joints.add(link.to);
        }
        const shaft = new THREE.CylinderGeometry(0.42, 0.46, PIPE_Y + 0.45, 24);
        const cover = new THREE.CylinderGeometry(0.3, 0.3, 0.04, 24);
        for (const id of joints) {
            if (id === net.wwtp || net.overflowPoints.includes(id)) continue;
            const p = this.pos.get(id);
            const s = this.add(shaft, this.mat.concrete);
            s.position.copy(p).setY((PIPE_Y + 0.45) / 2);
            const c = this.add(cover, this.mat.castIron, { shadow: false });
            c.position.copy(p).setY(PIPE_Y + 0.47);
        }
    }

    /**
     * Which way a tank's cut-open side faces (a turn about y; 0 is +z): as near the overview camera
     * as it can be while no pipe or overflow channel meets the tank in the open part of the wall.
     */
    cutFacing(id, p, cut) {
        const dirs = this.network.links
            .flatMap(l => l.from === id ? [l.to] : l.to === id ? [l.from] : [])
            .map(n => this.pos.get(n)).filter(Boolean).map(q => q.clone().sub(p));
        if (this.network.overflowPoints.includes(id)) dirs.push(this.nearestWater(p).point.clone().sub(p));
        const angles = dirs.filter(d => d.lengthSq() > 1e-6).map(d => Math.atan2(d.x, d.z));
        const gap = (a, b) => Math.abs(Math.atan2(Math.sin(a - b), Math.cos(a - b)));
        const clearance = theta => Math.min(Math.PI, ...angles.map(a => gap(theta, a)));
        const preferred = Math.atan2(-0.02, 0.62);
        let best = preferred, bestScore = -Infinity;
        for (let k = -314; k <= 314; k++) {
            const theta = k / 100, c = clearance(theta);
            const score = c >= cut / 2 + 0.35 ? 10 - gap(theta, preferred) : c;
            if (score > bestScore) { bestScore = score; best = theta; }
        }
        return best;
    }

    buildTanks() {
        const depthM = this.network.tankMaxDepthM;
        const model = M.tankModel(TANK_R, TANK_H, depthM, 1);
        const gaugeMaterial = new THREE.MeshBasicMaterial({ color: 0xe8eef6 });
        const waterBody = new THREE.CylinderGeometry(TANK_R - 0.01, TANK_R - 0.01, 1, 96);
        waterBody.translate(0, 0.5, 0);
        this.waterNormals = this.tex.waterNormals.clone();
        this.waterNormals.needsUpdate = true;
        this.waterNormals.repeat.set(1.6, 1.6);
        for (const id of this.network.tanks) {
            const p = this.pos.get(id);
            if (!p) continue;
            const group = new THREE.Group();
            group.position.copy(p);
            group.rotation.y = this.cutFacing(id, p, model.phiStart * 2);
            this.add(model.wall, this.mat.concreteTank, { parent: group });
            const band = new THREE.CylinderGeometry(TANK_R + model.thickness + 0.04, TANK_R + model.thickness + 0.04, 0.42, 64, 1, true, model.phiStart, model.phiLength);
            this.add(band, this.mat.band, { parent: group }).position.y = TANK_H * 0.68;
            this.add(model.rail, this.mat.steel, { parent: group });
            this.add(model.ladder, this.mat.steel, { parent: group });
            this.add(model.gauge, gaugeMaterial, { parent: group, shadow: false });
            const sensorMaterial = new THREE.MeshStandardMaterial({ color: 0xd9dde2, roughness: 0.4, metalness: 0.5, emissive: 0xffa23a, emissiveIntensity: 0 });
            this.add(model.sensor, sensorMaterial, { parent: group });

            const waterMaterial = new THREE.MeshPhysicalMaterial({
                color: 0x1e64a8, roughness: 0.1, metalness: 0, transparent: true, opacity: 0.9,
                emissive: 0x08305c, emissiveIntensity: 0.45, clearcoat: 1, clearcoatRoughness: 0.15,
            });
            const water = this.add(waterBody, waterMaterial, { parent: group, shadow: false });
            water.position.y = 0.03;
            water.scale.y = 0.001;
            const topMaterial = flat(0x3b9be0, { roughness: 0.35, metalness: 0.05, emissive: 0x0b3c74, emissiveIntensity: 0.25 });
            this.tankWater ??= this.waterDisc(TANK_R - 0.01);
            const top = this.add(this.tankWater, topMaterial, { parent: group, shadow: false });
            const sensorLine = new THREE.Mesh(new THREE.TorusGeometry(TANK_R - 0.02, 0.05, 8, 96, model.phiLength),
                new THREE.MeshBasicMaterial({ color: 0xffa23a }));
            sensorLine.rotation.x = Math.PI / 2;
            sensorLine.rotation.z = -(model.phiStart + model.phiLength - Math.PI / 2);
            sensorLine.visible = false;
            group.add(sensorLine);

            const beacon = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.glow, color: COLOR.alarm.high, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
            beacon.scale.set(3.2, 3.2, 1);
            beacon.position.y = TANK_H + 2.2;
            beacon.visible = false;
            group.add(beacon);

            const curtainMap = this.curtain.clone();
            curtainMap.needsUpdate = true;
            curtainMap.repeat.set(10, 1.4);
            const curtainMaterial = new THREE.MeshStandardMaterial({
                color: 0xd6e8ee, emissive: 0x22323a, roughness: 0.2, metalness: 0, alphaMap: curtainMap,
                transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide,
            });
            const curtain = new THREE.Mesh(new THREE.CylinderGeometry(TANK_R + 0.46, TANK_R + 0.62, TANK_H + 0.1, 96, 1, true), curtainMaterial);
            curtain.position.y = (TANK_H + 0.1) / 2 + 0.06;
            curtain.visible = false;
            group.add(curtain);
            const foamMaterial = new THREE.MeshBasicMaterial({ color: 0xeef6f8, map: this.glow, transparent: true, opacity: 0, depthWrite: false });
            const foam = new THREE.Mesh(new THREE.RingGeometry(TANK_R + 0.35, TANK_R + 1.4, 96), foamMaterial);
            foam.rotation.x = -Math.PI / 2;
            foam.position.y = 0.06;
            foam.visible = false;
            group.add(foam);
            const spill = { curtain, curtainMap, curtainMaterial, foam, foamMaterial, level: 0 };

            const hit = new THREE.Mesh(new THREE.CylinderGeometry(TANK_R + 0.6, TANK_R + 0.6, TANK_H + 1, 16), new THREE.MeshBasicMaterial({ visible: false }));
            hit.position.y = (TANK_H + 1) / 2;
            group.add(hit);
            this.pickable(hit, id);
            this.scene.add(group);
            this.tanks.set(id, {
                group, water, waterMaterial, top, topMaterial, sensorLine, sensorMaterial, beacon, spill,
                fill: 0, target: 0, sensorFill: null,
                label: this.label(id, p, TANK_H + 1.4, 'tank'),
            });
        }
    }

    buildGates() {
        const controlled = new Map(this.network.stations.map(s => [s.valve, s]));
        const big = M.gateModel(1.4, 1.0), small = M.gateModel(0.9, 0.7);
        for (const link of this.network.links) {
            if (link.kind !== 'orifice' || !this.tankSet.has(link.from)) continue;
            const a = this.pos.get(link.from), b = this.pos.get(link.to);
            if (!a || !b) continue;
            const dir = b.clone().sub(a).setY(0).normalize();
            const at = a.clone().addScaledVector(dir, TANK_R + 1.7);
            const group = new THREE.Group();
            group.position.copy(at);
            group.rotation.y = Math.atan2(dir.x, dir.z);
            const isControlled = controlled.has(link.id);
            const model = isControlled ? big : small;
            this.add(model.concrete, this.mat.concrete, { parent: group });
            this.add(model.steel, this.mat.steel, { parent: group });
            this.add(model.paint, isControlled ? this.mat.paint : this.mat.darkSteel, { parent: group });
            this.add(model.wheel, this.mat.yellow, { parent: group });
            const entry = { group, controlled: isControlled, base: model.plateBase, travel: model.travel, opening: 0.5, target: 0.5, command: null };
            const plateMaterial = new THREE.MeshStandardMaterial({ color: COLOR.gateOk, roughness: 0.4, metalness: 0.55, emissive: 0x000000 });
            const plate = this.add(model.plate, plateMaterial, { parent: group });
            plate.position.y = model.plateBase + 0.5 * model.travel;
            if (isControlled) {
                const ghost = new THREE.Mesh(model.plate, new THREE.MeshBasicMaterial({ color: 0xffffff, wireframe: true, transparent: true, opacity: 0 }));
                group.add(ghost);
                Object.assign(entry, { plate, plateMaterial, ghost });
                const hit = new THREE.Mesh(new THREE.BoxGeometry(2.4, model.top, 1.4), new THREE.MeshBasicMaterial({ visible: false }));
                hit.position.y = model.top / 2;
                group.add(hit);
                this.pickable(hit, link.id);
                entry.label = this.label(link.id, at, model.top + 0.5, 'gate');
            }
            this.scene.add(group);
            this.gates.set(link.id, entry);
        }
    }

    buildWeirs() {
        const model = M.chamberModel();
        const kiosk = new THREE.MeshStandardMaterial({ color: 0xc4c9cf, roughness: 0.5, metalness: 0.3 });
        for (const id of this.network.overflowPoints) {
            if (this.tankSet.has(id)) continue;
            const p = this.pos.get(id);
            if (!p) continue;
            const group = new THREE.Group();
            group.position.copy(p);
            const chamber = this.add(model.concrete, this.mat.concrete, { parent: group });
            this.add(model.iron, this.mat.castIron, { parent: group });
            this.add(model.kiosk, kiosk, { parent: group });
            const boilMap = this.tex.waterNormals.clone();
            boilMap.needsUpdate = true;
            const boilMaterial = new THREE.MeshStandardMaterial({
                color: 0x7f9f9c, roughness: 0.15, normalMap: boilMap, normalScale: new THREE.Vector2(1.5, 1.5),
                transparent: true, opacity: 0, depthWrite: false, emissive: 0x1b2b2a,
            });
            const boil = new THREE.Mesh(new THREE.SphereGeometry(0.95, 32, 8, 0, Math.PI * 2, 0, Math.PI * 0.18), boilMaterial);
            boil.visible = false;
            const boilGroup = new THREE.Group();
            boilGroup.position.y = 0.05;
            boilGroup.add(boil);
            group.add(boilGroup);
            const spill = { boil, material: boilMaterial, map: boilMap, level: 0 };
            const beacon = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.glow, color: COLOR.alarm.high, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
            beacon.scale.set(2.4, 2.4, 1);
            beacon.position.y = 3.2;
            beacon.visible = false;
            group.add(beacon);
            this.pickable(chamber, id);
            this.scene.add(group);
            this.weirs.set(id, { group, spill, beacon, label: this.label(id, p, 2.4, 'weir') });
        }
    }

    /** Each overflow point drains to the nearest receiving water through an open concrete channel; brown while it spills. */
    buildChannels() {
        this.network.overflowPoints.forEach((id, i) => {
            const p = this.pos.get(id);
            if (!p) return;
            const { point, water, u } = this.nearestWater(p);
            const dir = point.clone().sub(p).setY(0);
            if (dir.length() < 0.5) return;
            dir.normalize();
            const start = p.clone().addScaledVector(dir, this.tankSet.has(id) ? TANK_R + 1.5 : 1.1).setY(0);
            const end = point.clone().setY(0);
            const length = end.distanceTo(start);
            if (length < 0.4 || end.clone().sub(start).dot(dir) <= 0) return;
            const mid = start.clone().lerp(end, 0.5);
            const angle = Math.atan2(dir.x, dir.z);
            const walls = this.add(M.merge([M.box(0.12, 0.22, length, -0.4, 0.11, 0), M.box(0.12, 0.22, length, 0.4, 0.11, 0)]), this.mat.concreteDark);
            walls.position.copy(mid);
            walls.rotation.y = angle;
            const bed = this.add(new THREE.PlaneGeometry(0.7, length), this.mat.concreteDark, { shadow: false });
            bed.rotation.order = 'YXZ';
            bed.rotation.set(-Math.PI / 2, angle, 0);
            bed.position.copy(mid).setY(0.04);
            const texture = this.tex.waterNormals.clone();
            texture.needsUpdate = true;
            texture.repeat.set(0.3, length / 3);
            const material = new THREE.MeshStandardMaterial({
                color: 0x6f8f8b, roughness: 0.15, normalMap: texture, normalScale: new THREE.Vector2(1.2, 1.2),
                emissive: 0x16221f, transparent: true, opacity: 0, depthWrite: false,
            });
            const flow = new THREE.Mesh(new THREE.PlaneGeometry(0.66, length), material);
            flow.rotation.order = 'YXZ';
            flow.rotation.set(-Math.PI / 2, angle + Math.PI, 0);
            flow.position.copy(mid).setY(0.12);
            this.scene.add(flow);
            water.spots.push(i % RIVER_SPOTS);
            this.channels.set(id, { texture, material, u, water, slot: i % RIVER_SPOTS, amount: 0, active: 0, a: start, b: end });
        });
    }

    buildPlant() {
        const p = this.pos.get(this.network.wwtp);
        if (!p) return;
        const group = new THREE.Group();
        group.position.copy(p).add(new THREE.Vector3(5, 0, 1));
        const waterMat = flat(0x3aa3c4, { roughness: 0.35, emissive: 0x0a3344, emissiveIntensity: 0.25 });
        this.clarifiers = [];
        const clarifier = M.clarifierModel(1.7);
        for (const [x, z] of [[0, -3.6], [4.4, -3.6]]) {
            this.add(clarifier.wall, this.mat.concrete, { parent: group }).position.set(x, 0, z);
            this.clarifierWater ??= this.waterDisc(1.7, 5, 20);
            const surface = this.add(this.clarifierWater, waterMat, { parent: group, shadow: false });
            surface.position.set(x, 0.72, z);
            const bridge = this.add(clarifier.bridge, this.mat.steel, { parent: group });
            bridge.position.set(x, 0, z);
            this.clarifiers.push(bridge);
        }
        const basin = this.add(M.basinModel(6, 3.2), this.mat.concrete, { parent: group });
        basin.position.set(2.2, 0, 1.4);
        for (const x of [-1.5, 1.5]) {
            this.basinWater ??= this.waterPlane(2.8, 3.0, 6, 6);
            const w = this.add(this.basinWater, waterMat, { parent: group, shadow: false });
            w.position.set(2.2 + x, 0.65, 1.4);
        }
        const bubbles = new Float32Array(400 * 3);
        for (let i = 0; i < 400; i++) bubbles.set([2.2 + (this.rand() - 0.5) * 5.6, 0.68, 1.4 + (this.rand() - 0.5) * 2.8], i * 3);
        const bubbleGeometry = new THREE.BufferGeometry();
        bubbleGeometry.setAttribute('position', new THREE.BufferAttribute(bubbles, 3));
        this.bubbles = new THREE.Points(bubbleGeometry, new THREE.PointsMaterial({ color: 0xcfefff, size: 0.09, transparent: true, opacity: 0.7, depthWrite: false }));
        group.add(this.bubbles);
        const digester = M.digesterModel();
        const cladding = flat(0xe9e2d0);
        const membrane = flat(0x7fa36a);
        for (const z of [-4.0, -1.0]) {
            const at = new THREE.Vector3(9.4, 0, z);
            this.add(digester.shell, cladding, { parent: group }).position.copy(at);
            this.add(digester.steel, this.mat.darkSteel, { parent: group }).position.copy(at);
            this.add(digester.rail, this.mat.steel, { parent: group }).position.copy(at);
            this.add(digester.stair, this.mat.concreteDark, { parent: group }).position.copy(at);
        }
        this.add(digester.holderBase, this.mat.concrete, { parent: group }).position.set(12.5, 0, -2.5);
        this.add(digester.membrane, membrane, { parent: group }).position.set(12.5, 0, -2.5);
        const halls = this.kits.industry ?? [];
        if (halls.length) {
            const proto = halls[1 % halls.length];
            instance(group, proto, [{ position: new THREE.Vector3(9.2, 0, 3.0), rotation: Math.PI, scale: 3.6 / Math.max(proto.size.x, proto.size.z) }]);
        } else {
            this.add(M.box(3.2, 2.2, 3.4, 0, 1.1, 0, 0.05), flat(0xc7ccd3), { parent: group }).position.set(9.2, 0, 3.0);
        }
        const yard = this.add(new THREE.PlaneGeometry(17.5, 11), flat(0xc4c8cc, { roughness: 1 }), { shadow: false, parent: group });
        yard.rotation.x = -Math.PI / 2;
        yard.position.set(5.6, 0.015, -0.6);
        this.scene.add(group);
        const hit = new THREE.Mesh(new THREE.BoxGeometry(14, 4, 10), new THREE.MeshBasicMaterial({ visible: false }));
        hit.position.set(4.6, 2, -0.6);
        group.add(hit);
        this.pickable(hit, 'wwtp');
        this.plantCenter = group.position.clone().add(new THREE.Vector3(4.6, 0, -0.6));
        this.plantLabel = this.label('Treatment plant', this.plantCenter, 4.4, 'plant', 'wwtp');
        for (const dx of [-8, 9]) for (const dz of [-6, 6]) this.extend(this.plantCenter.clone().add(new THREE.Vector3(dx, 0, dz)));

        const from = group.position.clone().add(new THREE.Vector3(2.2, 0, 3.2));
        const { point } = this.nearestWater(from);
        const cdir = point.clone().sub(from).setY(0).normalize();
        const start = from.clone().setY(0);
        const length = point.distanceTo(start);
        const cangle = Math.atan2(cdir.x, cdir.z);
        const cmid = start.clone().lerp(point, 0.5);
        const texture = this.tex.waterNormals.clone();
        texture.needsUpdate = true;
        texture.repeat.set(0.3, length / 3);
        const material = new THREE.MeshStandardMaterial({
            color: 0x2f7f96, roughness: 0.1, metalness: 0.1, normalMap: texture, emissive: 0x0a2a33,
            transparent: true, opacity: 0, depthWrite: false,
        });
        const cwalls = this.add(M.merge([M.box(0.12, 0.22, length, -0.4, 0.11, 0), M.box(0.12, 0.22, length, 0.4, 0.11, 0)]), this.mat.concreteDark);
        cwalls.position.copy(cmid);
        cwalls.rotation.y = cangle;
        const bed = this.add(new THREE.PlaneGeometry(0.7, length), this.mat.concreteDark, { shadow: false });
        bed.rotation.order = 'YXZ';
        bed.rotation.set(-Math.PI / 2, cangle, 0);
        bed.position.copy(cmid).setY(0.04);
        const flow = new THREE.Mesh(new THREE.PlaneGeometry(0.66, length), material);
        flow.rotation.order = 'YXZ';
        flow.rotation.set(-Math.PI / 2, cangle + Math.PI, 0);
        flow.position.copy(cmid).setY(0.12);
        this.scene.add(flow);
        this.cleanChannel = { texture, material, a: start, b: point.clone() };
        this.plantFootprint = { center: this.plantCenter, radius: 9.5 };
    }

    /** The field controller (PLC) next to the stations, the control room (SCADA) at the edge of town, the link between them. */
    buildControl() {
        const stations = this.network.stations.map(s => this.gates.get(s.valve)?.group.position).filter(Boolean);
        const mean = stations.reduce((acc, p) => acc.add(p), new THREE.Vector3()).multiplyScalar(1 / Math.max(1, stations.length));
        const plcAt = this.freeSpot(mean.clone().add(new THREE.Vector3(0, 0, -6)), 3.5);
        const cabinet = new THREE.Group();
        cabinet.position.copy(plcAt);
        const cm = M.cabinetModel();
        this.add(cm.plinth, this.mat.concrete, { parent: cabinet });
        const body = this.add(cm.body, new THREE.MeshStandardMaterial({ color: 0xcfd3d8, roughness: 0.38, metalness: 0.45 }), { parent: cabinet });
        this.add(cm.details, this.mat.darkSteel, { parent: cabinet });
        this.plcLedMaterial = new THREE.MeshBasicMaterial({ color: 0x36d399 });
        const led = new THREE.Mesh(M.box(0.5, 0.07, 0.02), this.plcLedMaterial);
        led.position.set(-0.25, 1.85, 0.32);
        cabinet.add(led);
        this.scene.add(cabinet);
        this.pickable(body, 'plc');
        this.plcLabel = this.label('PLC', plcAt, 4.6, 'plc', 'plc');

        this.cables = [];
        for (const s of this.network.stations) {
            const gate = this.gates.get(s.valve);
            if (!gate) continue;
            const a = plcAt.clone().setY(0.06), b = gate.group.position.clone().setY(0.06);
            const texture = this.stripes.clone();
            texture.needsUpdate = true;
            const material = new THREE.MeshBasicMaterial({ color: 0x1aa6c9, alphaMap: texture, transparent: true, opacity: 0.5, depthWrite: false });
            const cable = segment(a, b, 0.06, material, 8);
            texture.repeat.set(1, cable.length / 2.5);
            this.scene.add(cable.mesh);
            this.cables.push({ texture, material, a, b });
        }

        const bounds = this.bounds;
        const roomAt = this.freeSpot(new THREE.Vector3(bounds.maxX - 4, 0, bounds.minZ - 2), 5.5);
        const room = new THREE.Group();
        room.position.copy(roomAt);
        const rm = M.controlRoomModel();
        this.add(rm.base, this.mat.concrete, { parent: room });
        const offices = this.kits.centre ?? [];
        if (offices.length) {
            const proto = offices[offices.length - 1];
            instance(room, proto, [{ position: new THREE.Vector3(0, 0.25, 0), rotation: Math.PI, scale: 4.8 / Math.max(proto.size.x, proto.size.z) }]);
        } else {
            this.add(rm.body, flat(0x6a8fb5), { parent: room });
            this.add(rm.roof, this.mat.darkSteel, { parent: room });
        }
        const building = new THREE.Mesh(new THREE.BoxGeometry(5, 4, 4), new THREE.MeshBasicMaterial({ visible: false }));
        building.position.y = 2;
        room.add(building);
        this.add(rm.mast, this.mat.steel, { parent: room });
        this.add(rm.dish, new THREE.MeshStandardMaterial({ color: 0xe6e9ed, roughness: 0.4, metalness: 0.3, side: THREE.DoubleSide }), { parent: room });
        this.roomLight = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.glow, color: 0xff3b3b, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending }));
        this.roomLight.scale.set(1.2, 1.2, 1);
        this.roomLight.position.set(1.8, 9.15, 0);
        room.add(this.roomLight);
        this.scene.add(room);
        this.pickable(building, 'center');
        this.roomLabel = this.label('SCADA', roomAt, 4.4, 'room', 'center');

        const from = roomAt.clone().add(new THREE.Vector3(1.8, 9, 0));
        const to = plcAt.clone().add(new THREE.Vector3(0.5, 4.1, 0));
        const mid = from.clone().lerp(to, 0.5).setY(Math.max(from.y, to.y) + 9);
        const curve = new THREE.QuadraticBezierCurve3(from, mid, to);
        const texture = this.stripes.clone();
        texture.needsUpdate = true;
        const material = new THREE.MeshBasicMaterial({ color: COLOR.linkUp, alphaMap: texture, transparent: true, opacity: 0.9, depthWrite: false });
        const geometry = new THREE.TubeGeometry(curve, 120, 0.045, 6, false);
        const uv = geometry.attributes.uv;
        for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getY(i), uv.getX(i));
        texture.repeat.set(1, curve.getLength() / 0.9);
        this.scene.add(new THREE.Mesh(geometry, material));
        const breakMark = new THREE.Sprite(new THREE.SpriteMaterial({ map: this.crossTexture(), color: 0xff4d4f, transparent: true, depthWrite: false }));
        breakMark.position.copy(curve.getPoint(0.5));
        breakMark.scale.set(2.6, 2.6, 1);
        breakMark.visible = false;
        this.scene.add(breakMark);
        this.link = { texture, material, breakMark };
        this.plcAt = plcAt;
        this.roomAt = roomAt;
    }

    crossTexture() {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 64;
        const g = canvas.getContext('2d');
        g.strokeStyle = '#fff';
        g.lineWidth = 9;
        g.lineCap = 'round';
        g.beginPath();
        g.moveTo(14, 14); g.lineTo(50, 50); g.moveTo(50, 14); g.lineTo(14, 50);
        g.stroke();
        return new THREE.CanvasTexture(canvas);
    }

    /** A spot near `near` that keeps `clearance` from tanks, structures and pipes. */
    freeSpot(near, clearance) {
        const occupied = [...this.pos.values()];
        const ok = q => occupied.every(p => p.distanceTo(q) > clearance + TANK_R)
            && this.network.links.every(l => {
                const a = this.pos.get(l.from), b = this.pos.get(l.to);
                return !a || !b || distanceToSegment(q, a, b) > clearance * 0.6;
            });
        if (ok(near)) return near;
        for (let r = 2; r < 40; r += 1.5) {
            for (let k = 0; k < 16; k++) {
                const q = near.clone().add(new THREE.Vector3(Math.cos(k / 16 * Math.PI * 2) * r, 0, Math.sin(k / 16 * Math.PI * 2) * r));
                if (ok(q)) return q;
            }
        }
        return near;
    }

    /** Things houses, trees and streets keep clear of: circles and segments. */
    obstacles() {
        if (this._obstacles) return this._obstacles;
        const circles = [
            ...this.network.tanks.map(id => [this.pos.get(id), TANK_R + 1.4]),
            ...this.network.overflowPoints.filter(id => !this.tankSet.has(id)).map(id => [this.pos.get(id), 1.8]),
            ...[...this.gates.values()].map(g => [g.group.position, 1.6]),
            [this.plcAt, 1.8], [this.roomAt, 4.2],
        ].filter(([p]) => p);
        if (this.plantFootprint) circles.push([this.plantFootprint.center, this.plantFootprint.radius]);
        const segments = this.network.links.map(l => [this.pos.get(l.from), this.pos.get(l.to), 0.6]).filter(([a, b]) => a && b);
        for (const ch of this.channels.values()) segments.push([ch.a, ch.b, 0.6]);
        if (this.cleanChannel) segments.push([this.cleanChannel.a, this.cleanChannel.b, 0.5]);
        for (const g of this.gauges) circles.push([g.at, 1.0]);
        for (const c of this.cables ?? []) segments.push([c.a, c.b, 0.2]);
        this._obstacles = { circles, segments };
        return this._obstacles;
    }

    clearance(q) {
        const { circles, segments } = this.obstacles();
        let d = Infinity;
        for (const [p, r] of circles) d = Math.min(d, p.distanceTo(q) - r);
        for (const [a, b, w] of segments) d = Math.min(d, distanceToSegment(q, a, b) - w);
        for (const w of this.waters) {
            for (let i = 1; i < w.pts.length; i += 1) d = Math.min(d, distanceToSegment(q, w.pts[i - 1], w.pts[i]) - w.width / 2 - 1.2);
        }
        return d;
    }

    /** The subcatchment q lies in, or null. */
    catchmentAt(q) {
        return this.catchments.find(c => pointInPolygon(q.x, q.z, c.poly)) ?? null;
    }

    buildStreets() {
        const b = this.bounds;
        const x0 = b.minX - 40, z0 = b.minZ - 40;
        const nx = Math.ceil((b.maxX + 40 - x0) / TILE), nz = Math.ceil((b.maxZ + 40 - z0) / TILE);
        const every = { x: Math.round(BLOCK_X / TILE), z: Math.round(BLOCK_Z / TILE) };
        const centreOf = (i, j) => new THREE.Vector3(x0 + i * TILE, 0, z0 + j * TILE);
        const { circles } = this.obstacles();
        const blocked = q => circles.some(([p, r]) => p.distanceTo(q) < r + ROAD_W / 2 + 0.3)
            || this.waters.some(w => w.pts.some((pt, i) => i > 0 && distanceToSegment(q, w.pts[i - 1], pt) < w.width / 2 + ROAD_W / 2 + 0.8))
            || [...this.channels.values()].some(ch => distanceToSegment(q, ch.a, ch.b) < ROAD_W / 2 + 0.6);
        const usable = q => {
            const c = this.catchmentAt(q);
            return c && distanceToPolygonEdge(q, c.poly) > 0.8 && !blocked(q);
        };
        const road = new Set();
        const key = (i, j) => i * 100000 + j;
        for (let i = 0; i < nx; i++) {
            for (let j = 0; j < nz; j++) {
                if ((i % every.x === 0 || j % every.z === 0) && usable(centreOf(i, j))) road.add(key(i, j));
            }
        }
        const SIDES = [[1, 0], [0, 1], [-1, 0], [0, -1]];
        const links = (i, j) => SIDES.map(([di, dj]) => road.has(key(i + di, j + dj)));
        const count = (i, j) => links(i, j).filter(Boolean).length;
        const cells = () => [...road].map(k => [Math.floor(k / 100000), k % 100000]);
        for (let pass = 0; pass < 3; pass++) {
            for (const [i, j] of cells()) {
                const n = count(i, j);
                const stub = n === 1 && SIDES.some(([di, dj], s) => links(i, j)[s] && count(i + di, j + dj) >= 3);
                if (n === 0 || stub) road.delete(key(i, j));
            }
        }
        const seen = new Set();
        for (const [i, j] of cells()) {
            if (seen.has(key(i, j))) continue;
            const part = [], stack = [[i, j]];
            seen.add(key(i, j));
            while (stack.length) {
                const [a, c] = stack.pop();
                part.push(key(a, c));
                for (const [di, dj] of SIDES) {
                    const k = key(a + di, c + dj);
                    if (road.has(k) && !seen.has(k)) { seen.add(k); stack.push([a + di, c + dj]); }
                }
            }
            if (part.length < 4) for (const k of part) road.delete(k);
        }

        this.roadRuns = [];
        for (let j = 0; j < nz; j += every.z) {
            for (let i = 0; i < nx; i++) {
                if (!road.has(key(i, j)) || !(road.has(key(i + 1, j)) || road.has(key(i - 1, j)))) continue;
                const from = i;
                while (road.has(key(i + 1, j))) i++;
                this.roadRuns.push({ horizontal: true, fixed: z0 + j * TILE, from: x0 + from * TILE - TILE / 2, to: x0 + i * TILE + TILE / 2 });
            }
        }
        for (let i = 0; i < nx; i += every.x) {
            for (let j = 0; j < nz; j++) {
                if (!road.has(key(i, j)) || !(road.has(key(i, j + 1)) || road.has(key(i, j - 1)))) continue;
                const from = j;
                while (road.has(key(i, j + 1))) j++;
                this.roadRuns.push({ horizontal: false, fixed: x0 + i * TILE, from: z0 + from * TILE - TILE / 2, to: z0 + j * TILE + TILE / 2 });
            }
        }

        const OPEN = {
            'road-straight': [1, 0, 1, 0], 'road-crossing': [1, 0, 1, 0], 'road-crossroad': [1, 1, 1, 1],
            'road-intersection': [1, 1, 1, 0], 'road-bend': [0, 1, 1, 0], 'road-end-round': [1, 0, 0, 0],
        };
        const turn = (open, want) => {
            for (let r = 0; r < 4; r++) {
                if (want.every((w, s) => w === open[(s + r) % 4])) return r * Math.PI / 2;
            }
            return 0;
        };
        const centre = this.places.find(pl => /town centre/i.test(pl.name))?.at;
        const tiles = new Map(), lamps = [];
        for (const [i, j] of cells()) {
            const want = links(i, j).map(Number);
            const n = want.reduce((a, c) => a + c, 0);
            const q = centreOf(i, j);
            let name = n === 4 ? 'road-crossroad' : n === 3 ? 'road-intersection' : n === 1 ? 'road-end-round'
                : want[0] === want[2] ? 'road-straight' : 'road-bend';
            if (name === 'road-straight' && centre && q.distanceTo(centre) < 16
                && SIDES.some(([di, dj], s) => want[s] && count(i + di, j + dj) >= 3)) name = 'road-crossing';
            if (!tiles.has(name)) tiles.set(name, []);
            tiles.get(name).push({ position: q.clone().setY(0.016), rotation: turn(OPEN[name], want), scale: TILE });
            if (name === 'road-straight' && (i + j) % 3 === 0) {
                lamps.push(want[0]
                    ? { position: q.clone().add(new THREE.Vector3(0, 0.016, TILE * 0.45)), rotation: 0, scale: TILE }
                    : { position: q.clone().add(new THREE.Vector3(-TILE * 0.45, 0.016, 0)), rotation: -Math.PI / 2, scale: TILE });
            }
        }
        const kit = this.kits.roads ?? {};
        for (const [name, placements] of tiles) {
            if (kit[name]) {
                instance(this.scene, kit[name], placements, { shadow: false });
                continue;
            }
            const mesh = new THREE.InstancedMesh(new THREE.BoxGeometry(TILE, 0.03, TILE), flat(0x4b5059, { roughness: 0.9 }), placements.length);
            const m = new THREE.Matrix4();
            placements.forEach((pl, k) => mesh.setMatrixAt(k, m.makeTranslation(pl.position.x, 0.03, pl.position.z)));
            mesh.receiveShadow = true;
            this.scene.add(mesh);
        }
        if (this.kits.lamps?.[0]) instance(this.scene, this.kits.lamps[0], lamps);
    }

    buildTown() {
        const rand = this.rand;
        const kits = this.kits;
        const b = this.bounds;
        const centre = this.places.find(pl => /town centre/i.test(pl.name))?.at;
        const street = q => {
            let best = Infinity, ry = 0;
            for (const r of this.roadRuns ?? []) {
                const along = r.horizontal ? q.x : q.z, across = r.horizontal ? q.z - r.fixed : q.x - r.fixed;
                const off = Math.max(0, r.from - along, along - r.to);
                const d = Math.hypot(off, across);
                if (d < best) { best = d; ry = r.horizontal ? (across < 0 ? 0 : Math.PI) : (across < 0 ? Math.PI / 2 : -Math.PI / 2); }
            }
            return { d: best, ry };
        };
        const roadDistance = q => street(q).d;
        const facing = q => street(q).ry;
        const placed = [];
        const free = (q, r) => !placed.some(([p, pr]) => Math.hypot(p.x - q.x, p.z - q.z) < r + pr);
        const groups = new Map();
        const put = (kit, k, variation, placement) => {
            const key = `${kit}:${k}:${variation}`;
            if (!groups.has(key)) groups.set(key, { kit, k, variation, placements: [] });
            groups.get(key).placements.push(placement);
        };
        const houses = kits.houses ?? [], centreKit = kits.centre ?? [], towers = kits.towers ?? [];
        const variations = kits.houseVariations ?? [];
        const radius = q => centre && q.distanceTo(centre) < 15 ? 1.25 : 0.92;
        const build = q => {
            const c = this.catchmentAt(q);
            if (!c) return;
            const r = radius(q);
            if (distanceToPolygonEdge(q, c.poly) < r * 0.8) return;
            if (roadDistance(q) < ROAD_W / 2 + 0.15 + r) return;
            if (this.clearance(q) < r + 0.2 || !free(q, r)) return;
            placed.push([q, r]);
            const ry = facing(q);
            if (r > 1 && centreKit.length) {
                const tall = towers.length && rand() < 0.1;
                const list = tall ? towers : centreKit;
                const k = Math.floor(rand() * list.length);
                const proto = list[k];
                put(tall ? 'towers' : 'centre', k, -1, { position: q, rotation: ry, scale: 2.5 / Math.max(proto.size.x, proto.size.z) });
            } else if (houses.length) {
                const k = Math.floor(rand() * houses.length);
                const proto = houses[k];
                const v = Math.floor(rand() * (variations.length + 1)) - 1;
                put('houses', k, v, { position: q, rotation: ry, scale: 1.75 / Math.max(proto.size.x, proto.size.z) });
            }
        };
        for (const run of this.roadRuns ?? []) {
            for (const side of [-1, 1]) {
                for (let d = run.from + 1.2; d < run.to - 1; d += 2.3 + rand() * 0.3) {
                    if (rand() < 0.1) continue;
                    const probe = run.horizontal ? new THREE.Vector3(d, 0, run.fixed) : new THREE.Vector3(run.fixed, 0, d);
                    const off = side * (ROAD_W / 2 + 0.2 + radius(probe));
                    build(run.horizontal ? new THREE.Vector3(d, 0, run.fixed + off) : new THREE.Vector3(run.fixed + off, 0, d));
                }
            }
        }
        const step = 1.85;
        for (let x = b.minX - 2; x <= b.maxX + 2; x += step) {
            for (let z = b.minZ - 2; z <= b.maxZ + 2; z += step) {
                if (rand() < 0.3) continue;
                build(new THREE.Vector3(x + (rand() - 0.5) * 0.4, 0, z + (rand() - 0.5) * 0.4));
            }
        }
        for (const { kit, k, variation, placements } of groups.values()) {
            instance(this.scene, kits[kit][k], placements, { map: variation >= 0 ? variations[variation] : null });
        }

        const trees = kits.trees ?? [], bushes = kits.bushes ?? [];
        if (!trees.length) return;
        const treeGroups = new Map();
        const plant = (list, key, q, height) => {
            const k = Math.floor(rand() * list.length);
            const proto = list[k];
            if (!treeGroups.has(key + k)) treeGroups.set(key + k, { proto, placements: [] });
            treeGroups.get(key + k).placements.push({ position: q, rotation: rand() * Math.PI * 2, scale: height / proto.size.y });
        };
        const park = this.areas.find(a => /park/i.test(a.name));
        for (let x = b.minX - 45; x <= b.maxX + 45; x += 1.7) {
            for (let z = b.minZ - 35; z <= b.maxZ + 35; z += 1.7) {
                const q = new THREE.Vector3(x + (rand() - 0.5) * 1.2, 0, z + (rand() - 0.5) * 1.2);
                const inPark = park && pointInPolygon(q.x, q.z, park.poly);
                const urbanArea = this.catchmentAt(q);
                let chance = inPark ? 0.32 : urbanArea ? 0.05 : 0.07;
                const water = this.nearestWater(q).distance;
                if (!urbanArea && water < 4 && water > 0.6) chance = 0.3;
                if (rand() > chance) continue;
                if (this.clearance(q) < 0.6 || !free(q, 0.45)) continue;
                if (urbanArea && roadDistance(q) < ROAD_W / 2 + 0.3) continue;
                placed.push([q, 0.45]);
                if (bushes.length && rand() < 0.25) plant(bushes, 'b', q, 0.5 + rand() * 0.4);
                else plant(trees, 't', q, 1.6 + rand() * 1.6);
            }
        }
        for (const { proto, placements } of treeGroups.values()) instance(this.scene, proto, placements);
    }

    buildSplashes() {
        const positions = new Float32Array(SPLASHES * 3), colors = new Float32Array(SPLASHES * 3);
        this.splashSeeds = Float32Array.from({ length: SPLASHES }, () => this.rand());
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.BufferAttribute(positions, 3));
        g.setAttribute('color', new THREE.BufferAttribute(colors, 3));
        this.splashes = new THREE.Points(g, new THREE.PointsMaterial({
            size: 0.22, map: this.glow, vertexColors: true, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
        }));
        this.splashes.frustumCulled = false;
        this.scene.add(this.splashes);
        this.splashCycle = new Int32Array(SPLASHES).fill(-1);
    }

    animateSplashes(t) {
        if (!this.splashes) return;
        const level = this.rainLevel ?? 0;
        const pos = this.splashes.geometry.attributes.position.array, col = this.splashes.geometry.attributes.color.array;
        const r = this.rainBox;
        const active = Math.floor(SPLASHES * Math.min(1, level * 1.3));
        for (let i = 0; i < SPLASHES; i++) {
            const phase = t * 2.2 + this.splashSeeds[i] * 7;
            const cycle = Math.floor(phase);
            if (i >= active) {
                col[i * 3] = col[i * 3 + 1] = col[i * 3 + 2] = 0;
                continue;
            }
            if (this.splashCycle[i] !== cycle) {
                this.splashCycle[i] = cycle;
                const x = r.x0 + 20 + Math.random() * (r.x1 - r.x0 - 40), z = r.z0 + 20 + Math.random() * (r.z1 - r.z0 - 30);
                pos.set([x, 0.08, z], i * 3);
            }
            const f = phase - cycle;
            const b = f < 0.15 ? f / 0.15 : Math.max(0, 1 - (f - 0.15) / 0.35);
            col[i * 3] = 0.55 * b; col[i * 3 + 1] = 0.7 * b; col[i * 3 + 2] = 0.9 * b;
        }
        this.splashes.geometry.attributes.position.needsUpdate = true;
        this.splashes.geometry.attributes.color.needsUpdate = true;
    }

    /** Film: start low over the river, rise to the whole town. */
    openingShot() {
        const w = this.waters[0];
        const low = w.pts[Math.floor(w.pts.length * 0.3)];
        const b = this.bounds;
        const target = new THREE.Vector3((b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2);
        this.controls.target.copy(low).setY(1);
        this.camera.position.copy(low).add(new THREE.Vector3(-18, 6, 16));
        this.controls.update(0);
        const position = target.clone().add(new THREE.Vector3(-2, 84, 74));
        this.tween = { t: 0, fromTarget: this.controls.target.clone(), toTarget: target, fromPos: this.camera.position.clone(), toPos: position, slow: true };
    }

    buildRain() {
        const b = this.bounds;
        this.rainBox = { x0: b.minX - 35, x1: b.maxX + 35, z0: b.minZ - 30, z1: b.maxZ + 20, top: 45 };
        const positions = new Float32Array(RAIN_DROPS * 6);
        this.rainSpeed = new Float32Array(RAIN_DROPS);
        const r = this.rainBox;
        for (let i = 0; i < RAIN_DROPS; i++) {
            const x = r.x0 + this.rand() * (r.x1 - r.x0);
            const y = this.rand() * r.top;
            const z = r.z0 + this.rand() * (r.z1 - r.z0);
            positions.set([x, y, z, x - 0.08, y + 0.9, z], i * 6);
            this.rainSpeed[i] = 34 + this.rand() * 14;
        }
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
        geometry.setDrawRange(0, 0);
        this.rain = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ color: 0xa8ccff, transparent: true, opacity: 0.32, depthWrite: false }));
        this.rain.frustumCulled = false;
        this.scene.add(this.rain);
        this.rainLevel = 0;
    }

    buildSelection() {
        const ring = new THREE.Mesh(new THREE.RingGeometry(1, 1.1, 96), new THREE.MeshBasicMaterial({ color: COLOR.select, transparent: true, opacity: 0.9, side: THREE.DoubleSide, depthWrite: false }));
        ring.rotation.x = -Math.PI / 2;
        ring.position.y = 0.12;
        ring.visible = false;
        this.scene.add(ring);
        this.selectionRing = ring;
    }

    bindPointer() {
        const canvas = this.renderer.domElement;
        let down = null;
        this.onDown = e => { down = { x: e.clientX, y: e.clientY }; this.lastInteraction = performance.now(); };
        this.onUp = e => {
            if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > 5) return;
            const rect = canvas.getBoundingClientRect();
            const ndc = new THREE.Vector2(((e.clientX - rect.left) / rect.width) * 2 - 1, -((e.clientY - rect.top) / rect.height) * 2 + 1);
            const ray = new THREE.Raycaster();
            ray.setFromCamera(ndc, this.camera);
            const hit = ray.intersectObjects(this.pickables, false)[0];
            this.pick(hit ? hit.object.userData.pick : null);
        };
        canvas.addEventListener('pointerdown', this.onDown);
        canvas.addEventListener('pointerup', this.onUp);
    }

    pick(id) {
        this.lastInteraction = performance.now();
        this.dotnet.invokeMethodAsync('Pick', id).catch(() => { });
    }

    anchorOf(id) {
        if (this.tanks.has(id)) return { p: this.tanks.get(id).group.position, r: TANK_R + 1.4, d: 24 };
        if (this.gates.has(id)) return { p: this.gates.get(id).group.position, r: 1.6, d: 20 };
        if (this.weirs.has(id)) return { p: this.weirs.get(id).group.position, r: 1.8, d: 14 };
        if (id === 'wwtp' && this.plantCenter) return { p: this.plantCenter, r: 8.5, d: 32 };
        if (id === 'plc') return { p: this.plcAt, r: 1.6, d: 18 };
        if (id === 'center') return { p: this.roomAt, r: 4, d: 26 };
        if (id === 'river') {
            const w = this.waters[0];
            return { p: w.pts[Math.floor(w.pts.length / 2)], r: 0, d: 60 };
        }
        return null;
    }

    focus(id) {
        this.selected = id;
        const a = id ? this.anchorOf(id) : null;
        this.selectionRing.visible = !FILM && !!(a && a.r > 0);
        if (!a) return;
        if (a.r > 0) {
            this.selectionRing.position.set(a.p.x, 0.12, a.p.z);
            this.selectionRing.scale.setScalar(a.r);
        }
        const target = a.p.clone().setY(1.5);
        const offset = this.camera.position.clone().sub(this.controls.target).normalize().multiplyScalar(a.d);
        offset.y = Math.max(offset.y, a.d * 0.65);
        this.flyTo(target, target.clone().add(offset));
    }

    overview(instant = false) {
        const b = this.bounds;
        const target = new THREE.Vector3((b.minX + b.maxX) / 2, 0, (b.minZ + b.maxZ) / 2 + 1);
        const span = Math.max(b.maxX - b.minX, (b.maxZ - b.minZ) * 1.7);
        const position = target.clone().add(new THREE.Vector3(-0.02, 0.7, 0.62).multiplyScalar(span));
        this.selected = null;
        if (this.selectionRing) this.selectionRing.visible = false;
        if (instant) {
            this.controls.target.copy(target);
            this.camera.position.copy(position);
            this.controls.update();
        } else {
            this.flyTo(target, position);
        }
    }

    flyTo(target, position) {
        this.tween = {
            t: 0,
            fromTarget: this.controls.target.clone(), toTarget: target,
            fromPos: this.camera.position.clone(), toPos: position,
        };
        this.lastInteraction = performance.now();
    }

    resize() {
        const w = this.host.clientWidth || 1, h = this.host.clientHeight || 1;
        this.renderer.setSize(w, h, false);
        this.composer.setSize(w, h);
        const pr = this.renderer.getPixelRatio();
        this.gtao.setSize(Math.round(w * pr), Math.round(h * pr));
        this.bloom.resolution.set(w / 2, h / 2);
        if (this.tiltH) {
            this.tiltH.uniforms.h.value = 1.6 / w;
            this.tiltV.uniforms.v.value = 1.6 / h;
        }
        this.camera.aspect = w / h;
        this.camera.updateProjectionMatrix();
    }

    update(frame) {
        if (this.filmDry) return;
        this.frame = frame;
        for (const t of frame.tanks) {
            const tank = this.tanks.get(t.id);
            if (!tank) continue;
            const real = t.trueFill ?? t.fill;
            tank.target = real ?? 0;
            tank.sensorFill = t.trueFill != null ? t.fill : null;
            tank.overflowing = t.overflowing;
            tank.alarm = t.alarm;
            tank.spill.level = t.overflowing ? 1 : 0;
            const shown = real == null ? '--' : `${Math.round(real * 100)}%`;
            const state = t.alarm ? `alarm-${t.alarm}` : t.overflowing ? 'spill' : '';
            this.setLabel(tank.label, t.trueFill != null ? `${shown} (sensor ${Math.round((t.fill ?? 0) * 100)}%)` : shown, state);
        }
        for (const g of frame.gates) {
            const gate = this.gates.get(g.id);
            if (!gate || !gate.controlled) continue;
            gate.target = g.opening ?? g.setpoint ?? gate.target;
            gate.command = g.setpoint;
            gate.alarm = g.alarm;
            const text = g.alarm ? 'stuck' : g.setpoint == null ? '--' : `${Math.round((g.opening ?? g.setpoint) * 100)}% open`;
            this.setLabel(gate.label, text, g.alarm ? `alarm-${g.alarm}` : '');
        }
        for (const w of frame.weirs) {
            const weir = this.weirs.get(w.id);
            if (!weir) continue;
            weir.spill.level = w.overflowing ? 1 : 0;
            weir.alarm = w.alarm;
            this.setLabel(weir.label, '', w.alarm ? `alarm-${w.alarm}` : w.overflowing ? 'spill' : '');
        }
        for (const [id, ch] of this.channels) {
            const volume = frame.volumes?.[id] ?? 0;
            ch.amount = volume > 0.5 ? 0.12 + 0.88 * (1 - Math.exp(-volume / 900)) : 0;
            ch.active = frame.tanks.find(t => t.id === id)?.overflowing || frame.weirs.find(w => w.id === id)?.overflowing ? 1 : 0;
        }
        const cut = frame.link === 'cut' || frame.link === 'lost';
        this.setLabel(this.roomLabel, cut ? 'link cut' : '', cut ? 'alarm-high' : '');
        this.setLabel(this.plcLabel, frame.plc === 'offline' ? 'offline' : frame.plcLocal ? 'on its own' : '',
            frame.plc === 'offline' ? 'alarm-high' : frame.plcLocal ? 'alarm-medium' : '');
    }

    tick(dt) {
        this.time += dt;
        const t = this.time;
        const f = this.frame;
        const running = !!f?.running;
        const rainRate = f?.rain ?? 0;

        if (this.tween) {
            const tw = this.tween;
            tw.t = Math.min(1, tw.t + dt / (tw.slow ? 9 : FILM ? 3.4 : 1.1));
            const e = tw.t < 0.5 ? 4 * tw.t ** 3 : 1 - (-2 * tw.t + 2) ** 3 / 2;
            this.controls.target.lerpVectors(tw.fromTarget, tw.toTarget, e);
            this.camera.position.lerpVectors(tw.fromPos, tw.toPos, e);
            if (tw.t >= 1) this.tween = null;
        }
        if (FILM) {
            this.controls.autoRotate = !this.tween && !this.filmHold;
            this.controls.autoRotateSpeed = 0.45;
        } else {
            this.controls.autoRotate = !running && !this.tween && performance.now() - this.lastInteraction > 20000;
        }
        this.controls.update(dt);
        this.animateSplashes(t);

        const k = 1 - Math.exp(-dt * 5);
        this.tex.waterNormals.offset.set(-t * 0.02, t * 0.012);
        this.animateWater(t);
        for (const tank of this.tanks.values()) {
            tank.fill += (tank.target - tank.fill) * k;
            const h = Math.max(0.001, tank.fill * TANK_H);
            tank.water.scale.y = h;
            tank.water.visible = tank.fill > 0.004;
            tank.top.position.y = 0.03 + h + 0.005;
            tank.top.visible = tank.fill > 0.004;
            const spilling = tank.overflowing;
            tank.topMaterial.color.setHex(spilling ? 0x7f9f9c : 0x3b8ad0);
            tank.topMaterial.emissive.setHex(spilling ? 0x1b2b2a : 0x0b3c74);
            tank.waterMaterial.color.setHex(spilling ? 0x5f807e : 0x1e64a8);
            if (tank.sensorFill != null) {
                tank.sensorLine.visible = true;
                tank.sensorLine.position.y = 0.03 + Math.max(0.03, tank.sensorFill * TANK_H);
                tank.sensorMaterial.emissiveIntensity = 0.6 + 0.4 * Math.sin(t * 6);
            } else {
                tank.sensorLine.visible = false;
                tank.sensorMaterial.emissiveIntensity = 0;
            }
            this.beacon(tank.beacon, tank.alarm, t);
            const sp = tank.spill;
            sp.curtainMaterial.opacity += (sp.level * 0.7 - sp.curtainMaterial.opacity) * Math.min(1, dt * 3);
            sp.foamMaterial.opacity = sp.curtainMaterial.opacity * (0.55 + 0.15 * Math.sin(t * 5));
            sp.curtain.visible = sp.foam.visible = sp.curtainMaterial.opacity > 0.02;
            sp.curtainMap.offset.y += dt * 1.1;
        }
        for (const weir of this.weirs.values()) {
            this.beacon(weir.beacon, weir.alarm, t);
            const sp = weir.spill;
            sp.material.opacity += (sp.level * 0.92 - sp.material.opacity) * Math.min(1, dt * 3);
            sp.boil.visible = sp.material.opacity > 0.02;
            const pulse = 1 + 0.08 * Math.sin(t * 7 + weir.group.position.x);
            sp.boil.scale.set(pulse, 1 + 0.4 * Math.sin(t * 5), pulse);
        }

        for (const gate of this.gates.values()) {
            if (!gate.controlled) continue;
            gate.opening += (gate.target - gate.opening) * k;
            gate.plate.position.y = gate.base + gate.opening * gate.travel;
            const bad = !!gate.alarm;
            gate.plateMaterial.color.setHex(bad ? COLOR.gateBad : COLOR.gateOk);
            gate.plateMaterial.emissive.setHex(bad ? COLOR.gateBad : 0x000000);
            gate.plateMaterial.emissiveIntensity = bad ? 0.5 + 0.4 * Math.sin(t * 7) : 0;
            if (gate.command != null && Math.abs(gate.command - gate.opening) > 0.05) {
                gate.ghost.material.opacity = 0.7;
                gate.ghost.position.y = gate.base + gate.command * gate.travel;
            } else {
                gate.ghost.material.opacity = 0;
            }
        }

        const meanFill = this.tanks.size ? [...this.tanks.values()].reduce((a, x) => a + x.fill, 0) / this.tanks.size : 0;
        const wet = running ? Math.min(1, 0.15 + rainRate / 12 + meanFill * 0.8) : 0;
        for (const pipe of this.pipes) {
            let level = wet;
            if (pipe.isOutlet) {
                const tank = this.tanks.get(pipe.link.from);
                const gate = this.gates.get(pipe.link.id);
                const opening = gate?.controlled ? gate.opening : 0.6;
                level = running && tank ? Math.min(1, opening * 1.2) * Math.min(1, 0.15 + tank.fill * 3) : 0;
            }
            pipe.level += (level - pipe.level) * k;
            pipe.material.opacity = 0.04 + pipe.level * 0.5;
            pipe.texture.offset.y -= dt * (0.4 + 1.6 * pipe.level);
        }
        for (const ch of this.channels.values()) {
            ch.material.opacity += (ch.active * 0.92 - ch.material.opacity) * k;
            ch.texture.offset.y -= dt * 0.9;
        }
        if (this.cleanChannel) {
            this.cleanChannel.material.opacity += ((running ? 0.9 : 0) - this.cleanChannel.material.opacity) * k;
            this.cleanChannel.texture.offset.y -= dt * 0.7;
        }
        for (const bridge of this.clarifiers ?? []) bridge.rotation.y += dt * (running ? 0.35 : 0.06);
        if (this.bubbles) {
            const pos = this.bubbles.geometry.attributes.position.array;
            for (let i = 1; i < pos.length; i += 3) pos[i] = 0.68 + ((t * 0.6 + i * 0.137) % 1) * 0.08;
            this.bubbles.geometry.attributes.position.needsUpdate = true;
        }

        for (const w of this.waters) {
            w.uniforms.uSpots.value.forEach(sp => sp.set(0, 0));
            w.uniforms.uTime.value = t;
        }
        for (const ch of this.channels.values()) {
            const u = ch.water.uniforms;
            const sp = u.uSpots.value[ch.slot];
            sp.set(ch.u, Math.max(sp.y, ch.amount));
            u.uActive.value[ch.slot].x += (ch.active - u.uActive.value[ch.slot].x) * k;
        }

        const linkState = f?.link ?? 'unused';
        const cut = linkState === 'cut' || linkState === 'lost';
        for (const c of this.cables ?? []) {
            c.texture.offset.y -= dt * (running ? 1.4 : 0.3);
            c.material.opacity = f?.plc === 'offline' ? 0.05 : 0.5;
        }
        if (this.link) {
            this.link.material.color.setHex(cut ? COLOR.linkBad : linkState === 'up' ? COLOR.linkUp : COLOR.linkIdle);
            this.link.material.opacity = cut ? 0.25 + 0.25 * (Math.sin(t * 10) > 0 ? 1 : 0) : linkState === 'up' ? 0.9 : 0.25;
            this.link.texture.offset.y -= dt * (linkState === 'up' ? 2.2 : 0);
            this.link.breakMark.visible = cut;
        }
        this.plcLedMaterial?.color.setHex(f?.plc === 'offline' ? COLOR.gateBad : f?.plcLocal ? 0xffa23a : 0x36d399);
        if (this.roomLight) this.roomLight.material.opacity = Math.sin(t * 3) > 0.6 ? 1 : 0.15;

        this.rainLevel += ((rainRate > 0.05 ? Math.min(1, 0.08 + rainRate / 22) : 0) - this.rainLevel) * (1 - Math.exp(-dt * 2));
        this.animateRain(dt);
        if (rainRate > 18 && Math.random() < dt * 0.12) this.flash = 1;
        this.flash = Math.max(0, this.flash - dt * 5);
        const strobe = this.flash > 0 ? (Math.sin(this.flash * 40) > 0 ? this.flash : this.flash * 0.3) : 0;
        const storm = Math.min(1, this.rainLevel * 1.4);
        this.hemi.intensity = 0.9 - storm * 0.3 + strobe * 3;
        this.sun.intensity = 2.9 - storm * 2.1;
        this.scene.fog.density = 0.0016 + storm * 0.004;
        this.scene.fog.color.set(SKY.fog).lerp(new THREE.Color(SKY.stormFog), storm);
        for (const w of this.waters) {
            w.uniforms.uFogDensity.value = this.scene.fog.density;
            w.uniforms.uFog.value.copy(this.scene.fog.color);
        }
        this.skyUniforms.uStorm.value = storm;
        this.cloudMat.color.setRGB(1 - storm * 0.55, 1 - storm * 0.52, 1 - storm * 0.48);
        this.clouds.rotation.y += dt * 0.004 * (1 + storm * 3);

        if (this.selectionRing.visible) this.selectionRing.material.opacity = 0.55 + 0.35 * Math.sin(t * 4);

        this.composer.render();
        this.placeLabels();
    }

    beacon(sprite, alarm, t) {
        if (!alarm) {
            sprite.visible = false;
            return;
        }
        sprite.visible = true;
        sprite.material.color.setHex(COLOR.alarm[alarm] ?? COLOR.alarm.high);
        sprite.material.opacity = 0.55 + 0.45 * Math.sin(t * 6);
    }

    animateRain(dt) {
        const n = Math.floor(RAIN_DROPS * this.rainLevel);
        this.rain.geometry.setDrawRange(0, n * 2);
        if (n === 0) return;
        const pos = this.rain.geometry.attributes.position.array;
        const r = this.rainBox;
        for (let i = 0; i < n; i++) {
            const o = i * 6;
            let y = pos[o + 1] - this.rainSpeed[i] * dt;
            if (y < 0) y += r.top;
            pos[o + 1] = y;
            pos[o + 4] = y + 0.9;
        }
        this.rain.geometry.attributes.position.needsUpdate = true;
    }

    placeLabels() {
        const w = this.host.clientWidth, h = this.host.clientHeight;
        const v = new THREE.Vector3();
        const camera = this.camera;
        const placed = [];
        const visibleItems = [];
        for (const item of this.labels) {
            v.copy(item.anchor).project(camera);
            const distance = camera.position.distanceTo(item.anchor);
            const gate = item.el.classList.contains('gate');
            const wanted = !gate || item.cls.startsWith('alarm') || item.pick === this.selected;
            const visible = wanted && v.z < 1 && Math.abs(v.x) < 1.1 && Math.abs(v.y) < 1.1 && distance < 170;
            if (!visible) {
                if (item.shown !== false) { item.el.style.display = 'none'; item.shown = false; }
                continue;
            }
            if (item.shown !== true) { item.el.style.display = ''; item.shown = true; item.size = null; }
            if (!item.size || item.text !== item.sizedText) {
                item.size = { w: item.el.offsetWidth, h: item.el.offsetHeight };
                item.sizedText = item.text;
            }
            visibleItems.push({ item, x: (v.x * 0.5 + 0.5) * w, y: (-v.y * 0.5 + 0.5) * h, rank: this.rank(item) });
        }
        visibleItems.sort((a, b) => a.rank - b.rank || b.y - a.y);
        for (const { item, x, y } of visibleItems) {
            const lw = item.size.w, lh = item.size.h;
            const cx = Math.min(Math.max(x, lw / 2 + 6), w - lw / 2 - 6);
            let top = y - lh;
            for (let tries = 0; tries < 4; tries++) {
                const hit = placed.find(r => cx - lw / 2 < r.x1 && cx + lw / 2 > r.x0 && top < r.y1 && top + lh > r.y0);
                if (!hit) break;
                top = hit.y0 - lh - 2;
            }
            placed.push({ x0: cx - lw / 2, x1: cx + lw / 2, y0: top, y1: top + lh });
            item.el.style.transform = `translate(${(cx - lw / 2).toFixed(1)}px, ${top.toFixed(1)}px)`;
            item.el.classList.toggle('selected', item.pick === this.selected);
        }
    }

    rank(item) {
        if (item.cls.startsWith('alarm')) return 0;
        if (item.cls === 'spill') return 1;
        if (item.el.classList.contains('tank')) return 2;
        if (item.el.classList.contains('gate')) return 4;
        return 3;
    }

    dispose() {
        this.disposed = true;
        cancelAnimationFrame(this.raf);
        this.resizeObserver.disconnect();
        this.renderer.domElement.removeEventListener('pointerdown', this.onDown);
        this.renderer.domElement.removeEventListener('pointerup', this.onUp);
        this.controls.dispose();
        this.scene.traverse(o => {
            o.geometry?.dispose?.();
            const m = o.material;
            if (Array.isArray(m)) m.forEach(x => x.dispose()); else m?.dispose?.();
        });
        this.composer.dispose?.();
        this.renderer.dispose();
        this.host.replaceChildren();
    }
}
