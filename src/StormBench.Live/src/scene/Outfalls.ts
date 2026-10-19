import {
  DoubleSide,
  Group,
  type Material,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  type Object3D,
  Vector3,
  type WebGLProgramParametersWithUniforms
} from 'three';
import { flat } from '../assets/materials';
import { numberOf, place, roleOf } from '../assets/structures';
import { RIVER_SPOTS, RIVER_Y } from '../config';
import type { LiveFrame } from '../types';
import { BANK } from '../world/land';
import type { WaterPath } from '../world/water';
import type { SceneContext } from './context';
import type { Rivers } from './Rivers';

/** How much of a plume a spilled volume (m3) leaves in the river, 0..1. */
export function plumeAmount(volume: number): number {
  return volume > 0.5 ? 0.12 + 0.88 * (1 - Math.exp(-volume / 900)) : 0;
}

/** How far the stream throws (times the modelled throw) and how wide it runs (times the pipe's stream) at a flow of 0..1. */
export function streamShape(flow: number): { throw: number; width: number } {
  const f = Math.min(1, Math.max(0, flow));
  return { throw: 0.45 + 0.85 * f, width: 0.55 + 0.45 * f };
}

export interface OutfallEntry {
  readonly at: Vector3;
  readonly jet: Group;
  readonly splash: Group;
  readonly foam: Group;
  readonly ripples: readonly Group[];
  readonly fades: readonly Material[];
  readonly mouth: Vector3;
  readonly throwLength: number;
  readonly seed: number;
  readonly water: WaterPath;
  readonly u: number;
  readonly slot: number | null;
  amount: number;
  active: number;
  shown: number;
}

const SPILL = { color: 0x8a9a80, emissive: 0x141a12 } as const;
const CLEAN = { color: 0x7fbedc, emissive: 0x10303c } as const;
const FOAM = 0xe9f0ec;
const SAND = 0xe8d49a;
const RIPPLES = 3;
const WWTP = 'wwtp';
const SPACING = 3.2;
const ON_WATER = 0.11;

/**
 * Puts a part under a pivot at `at` in its parent, keeping where it is: the part keeps its own
 * transform (with the scale and offset of its compressed geometry) and the pivot moves and scales it.
 */
function pivot(part: Object3D, at: Vector3): Group {
  const group = new Group();
  group.position.copy(at);
  part.parent!.add(group);
  part.position.sub(at);
  group.add(part);
  return group;
}

/** A copy of a mesh next to it, with the same transform. */
function addClone(part: Mesh): Mesh {
  const copy = part.clone();
  part.parent!.add(copy);
  return copy;
}

/**
 * The stream's material: water that runs, streaks of white aeration travelling down it and
 * growing towards the bottom, its surface trembling. The flow lives in the shader, driven by uTime.
 */
function streamMaterial(look: { color: number; emissive: number }, time: { value: number }): MeshStandardMaterial {
  const material = new MeshStandardMaterial({
    ...look,
    roughness: 0.18,
    metalness: 0,
    flatShading: true,
    transparent: true,
    opacity: 0.92,
    side: DoubleSide
  });
  material.onBeforeCompile = (shader: WebGLProgramParametersWithUniforms) => {
    shader.uniforms['uTime'] = time;
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', '#include <common>\nuniform float uTime;\nvarying vec2 vStream;')
      .replace(
        '#include <begin_vertex>',
        '#include <begin_vertex>\nvStream = uv;\ntransformed += normal * 0.012 * uv.y * sin(uTime * 15.0 + uv.y * 22.0 + uv.x * 6.2832);'
      );
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform float uTime;\nvarying vec2 vStream;')
      .replace(
        '#include <color_fragment>',
        [
          '#include <color_fragment>',
          'float flow = fract(vStream.y * 2.6 - uTime * 1.7 + 0.14 * sin(vStream.x * 18.85 + uTime * 2.3));',
          'float streak = smoothstep(0.58, 0.92, flow) * (0.3 + 0.7 * vStream.y);',
          'diffuseColor.rgb = mix(diffuseColor.rgb, vec3(0.93, 0.96, 0.95), clamp(streak * 0.75 + vStream.y * vStream.y * 0.35, 0.0, 1.0));'
        ].join('\n')
      );
  };
  return material;
}

/**
 * Where the sewers reach the waters: a concrete headwall in the bank, the pipe's mouth in its face,
 * a chute below, the bank filled back round it; one for each overflow and one for the
 * treatment plant. While one runs, water falls from the mouth into the river (a farther and fuller
 * stream the harder it runs), splashing and sending rings out where it lands; an overflow's spill
 * also leaves its plume downstream.
 */
export class Outfalls {
  readonly entries = new Map<string, OutfallEntry>();
  private readonly time = { value: 0 };

  constructor({ scene, world, structures }: SceneContext, plant: Vector3 | null) {
    const sites: [string, Vector3, boolean][] = world.network.overflowPoints.flatMap(id => {
      const p = world.pos.get(id);
      return p ? [[id, p, true] as [string, Vector3, boolean]] : [];
    });
    if (plant) sites.push([WWTP, plant, false]);
    const placed: Vector3[] = [];
    const sand = flat(SAND, { roughness: 1 });
    const foamMaterial = new MeshStandardMaterial({
      color: FOAM,
      roughness: 0.6,
      flatShading: true,
      transparent: true,
      side: DoubleSide
    });
    sites.forEach(([id, p, dirty], i) => {
      const { centre, water, u, normal } = world.nearestWater(p);
      const out = p.clone().sub(centre).setY(0);
      if (out.lengthSq() < 1e-6) return;
      out.normalize();
      const along = new Vector3(-normal.z, 0, normal.x);
      const at = centre
        .clone()
        .addScaledVector(out, water.width / 2 + BANK)
        .setY(0);
      for (let k = 0; k < 6 && placed.some(q => q.distanceTo(at) < SPACING); k++) at.addScaledVector(along, SPACING);
      placed.push(at);
      const model = place(structures.outfall);
      model.position.copy(at);
      model.rotation.y = Math.atan2(-out.x, -out.z);
      (roleOf(model, 'fill') as Mesh).material = sand;
      const mouth = roleOf(model, 'mouth').position.clone();
      const jetPart = roleOf(model, 'jet') as Mesh;
      const throwLength = numberOf(jetPart, 'throw');
      const landing = new Vector3(mouth.x, RIVER_Y, mouth.z + throwLength);
      jetPart.material = streamMaterial(dirty ? SPILL : CLEAN, this.time);
      const splashPart = roleOf(model, 'splash') as Mesh;
      splashPart.material = foamMaterial;
      const foamPart = roleOf(model, 'foam') as Mesh;
      const ripplePart = roleOf(model, 'ripple') as Mesh;
      const fades: Material[] = [];
      const fading = (part: Mesh): Mesh => {
        const material = new MeshBasicMaterial({ color: FOAM, transparent: true, depthWrite: false, side: DoubleSide });
        fades.push(material);
        part.material = material;
        part.renderOrder = 2;
        return part;
      };
      const jet = pivot(jetPart, mouth);
      const splash = pivot(splashPart, landing);
      const foam = pivot(fading(foamPart), landing);
      const ripples = [ripplePart, ...Array.from({ length: RIPPLES - 1 }, () => addClone(ripplePart))].map(part =>
        pivot(fading(part), landing)
      );
      for (const part of [jetPart, splashPart, foamPart, ...ripples.map(r => r.children[0] as Mesh)]) {
        part.castShadow = false;
        part.receiveShadow = false;
      }
      for (const group of [jet, splash, foam, ...ripples]) group.visible = false;
      scene.add(model);
      this.entries.set(id, {
        at,
        jet,
        splash,
        foam,
        ripples,
        fades,
        mouth,
        throwLength,
        seed: i * 1.7,
        water,
        u,
        slot: dirty ? i % RIVER_SPOTS : null,
        amount: 0,
        active: 0,
        shown: 0
      });
    });
  }

  /** Takes the volume each overflow has spilled and whether it spills now; the plant runs with the storm. */
  update(frame: LiveFrame): void {
    for (const [id, o] of this.entries) {
      if (id === WWTP) {
        o.active = frame.running ? 0.5 : 0;
        continue;
      }
      o.amount = plumeAmount(frame.volumes?.[id] ?? 0);
      o.active =
        frame.tanks.find(t => t.id === id)?.overflowing || frame.weirs.find(w => w.id === id)?.overflowing ? 1 : 0;
    }
  }

  /** Runs the water of the outfalls that pour, and marks the overflows' plumes on the rivers. */
  tick(t: number, k: number, rivers: Rivers): void {
    this.time.value = t;
    for (const o of this.entries.values()) {
      o.shown += (o.active - o.shown) * k;
      const on = o.shown > 0.03;
      o.jet.visible = o.splash.visible = o.foam.visible = on;
      for (const ring of o.ripples) ring.visible = on;
      if (on) this.pour(o, t);
      if (o.slot === null) continue;
      const u = rivers.uniformsOf(o.water);
      const spot = u.uSpots.value[o.slot]!;
      spot.set(o.u, Math.max(spot.y, o.amount));
      const active = u.uActive.value[o.slot]!;
      active.x += (o.active - active.x) * k;
    }
  }

  /** Shapes the stream for the flow, and moves the splash, foam and rings to where it lands. */
  private pour(o: OutfallEntry, t: number): void {
    const flow = o.shown;
    const shape = streamShape(flow);
    o.jet.scale.set(shape.width, 1, shape.throw);
    const landing = o.mouth.z + o.throwLength * shape.throw;
    const s = o.seed;
    o.splash.position.set(o.mouth.x, RIVER_Y + 0.03, landing);
    o.splash.scale.set(
      shape.width * (1 + 0.14 * Math.sin(t * 23 + s)),
      (0.6 + 0.6 * flow) * (1 + 0.3 * Math.sin(t * 31 + s * 2)),
      shape.width * (1 + 0.14 * Math.sin(t * 19 + s))
    );
    o.splash.rotation.y = t * 1.3 + s;
    o.foam.position.set(o.mouth.x, RIVER_Y + ON_WATER, landing + 0.05);
    const breathe = shape.width * (0.75 + 0.06 * Math.sin(t * 5 + s));
    o.foam.scale.set(breathe, 1, breathe);
    o.foam.rotation.y = t * 0.25 + s;
    o.fades[0]!.opacity = 0.5 * Math.min(1, flow * 1.5);
    o.ripples.forEach((ring, k) => {
      const p = (t * 0.75 + k / RIPPLES + s) % 1;
      const size = (0.14 + 0.62 * p) * (0.6 + 0.6 * flow);
      ring.position.set(o.mouth.x, RIVER_Y + ON_WATER, landing + 0.08);
      ring.scale.set(size, 1, size);
      o.fades[k + 1]!.opacity = Math.pow(1 - p, 1.2) * 0.8 * Math.min(1, flow * 1.5);
    });
  }
}
