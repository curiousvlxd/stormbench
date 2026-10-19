import {
  AdditiveBlending,
  BufferAttribute,
  BufferGeometry,
  Color,
  DoubleSide,
  Mesh,
  MeshBasicMaterial,
  Points,
  PointsMaterial,
  type Scene,
  type Texture
} from 'three';
import { FOG_DENSITY, RAIN_DROPS, SKY, SPLASHES } from '../config';
import type { Stage } from '../render/Stage';
import type { Bounds } from '../world/World';

interface RainBox {
  readonly x0: number;
  readonly x1: number;
  readonly z0: number;
  readonly z1: number;
  readonly top: number;
}

const STREAK = 0.9;
const STREAK_WIDTH = 0.06;
const DROPS_PER_LEVEL = 1.8;
const NEAR_FADE = [4, 12] as const;
const STREAK_SLANT = 0.08;
const DROP_VERTICES = 8;
const DROP_INDICES = 12;
const RAIN_COLOR = 0xdfeaf6;
const RAIN_OPACITY = 0.55;
const LIGHTNING_RAIN = 18;
const STORM_FOG = new Color(SKY.stormFog);
const NIGHT_FOG = new Color(SKY.nightFog);
const WARM_FOG = new Color(SKY.warmFog);

/** How hard it looks to rain (0..1) for a rain rate in mm/h. */
export function rainTarget(rate: number): number {
  return rate > 0.05 ? Math.min(1, 0.08 + rate / 22) : 0;
}

/** How bright a splash is at `f` (0..1) through its short life: a quick flash, then a fade. */
export function splashBrightness(f: number): number {
  return f < 0.15 ? f / 0.15 : Math.max(0, 1 - (f - 0.15) / 0.35);
}

/**
 * The weather over the town: rain streaks as hard as the simulator reports, splashes on the
 * ground, the odd lightning flash in heavy rain, and the light and fog closing in as the storm builds.
 */
export class Weather {
  private readonly box: RainBox;
  private readonly rain: Mesh;
  private readonly speed: Float32Array;
  private readonly splashes: Points;
  private readonly splashSeeds: Float32Array;
  private readonly splashCycle: Int32Array;
  private level = 0;
  private flash = 0;

  constructor(scene: Scene, bounds: Bounds, glow: Texture, rand: () => number) {
    const b = bounds;
    this.box = { x0: b.minX - 35, x1: b.maxX + 35, z0: b.minZ - 30, z1: b.maxZ + 20, top: 45 };
    const r = this.box;
    const positions = new Float32Array(RAIN_DROPS * DROP_VERTICES * 3);
    const index = new Uint32Array(RAIN_DROPS * DROP_INDICES);
    this.speed = new Float32Array(RAIN_DROPS);
    const h = STREAK_WIDTH / 2;
    for (let i = 0; i < RAIN_DROPS; i++) {
      const x = r.x0 + rand() * (r.x1 - r.x0);
      const y = rand() * r.top;
      const z = r.z0 + rand() * (r.z1 - r.z0);
      const t = x - STREAK_SLANT;
      positions.set(
        [
          x - h,
          y,
          z,
          x + h,
          y,
          z,
          t - h,
          y + STREAK,
          z,
          t + h,
          y + STREAK,
          z,
          x,
          y,
          z - h,
          x,
          y,
          z + h,
          t,
          y + STREAK,
          z - h,
          t,
          y + STREAK,
          z + h
        ],
        i * DROP_VERTICES * 3
      );
      const v = i * DROP_VERTICES;
      index.set([v, v + 1, v + 3, v, v + 3, v + 2, v + 4, v + 5, v + 7, v + 4, v + 7, v + 6], i * DROP_INDICES);
      this.speed[i] = 34 + rand() * 14;
    }
    const geometry = new BufferGeometry();
    geometry.setAttribute('position', new BufferAttribute(positions, 3));
    geometry.setIndex(new BufferAttribute(index, 1));
    geometry.setDrawRange(0, 0);
    const material = new MeshBasicMaterial({
      color: RAIN_COLOR,
      transparent: true,
      opacity: RAIN_OPACITY,
      depthWrite: false,
      side: DoubleSide
    });
    material.onBeforeCompile = shader => {
      shader.vertexShader = shader.vertexShader
        .replace('#include <common>', '#include <common>\nvarying float vNear;')
        .replace('#include <project_vertex>', '#include <project_vertex>\nvNear = -mvPosition.z;');
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nvarying float vNear;')
        .replace(
          '#include <opaque_fragment>',
          `diffuseColor.a *= smoothstep(${NEAR_FADE[0].toFixed(1)}, ${NEAR_FADE[1].toFixed(1)}, vNear);\n#include <opaque_fragment>`
        );
    };
    this.rain = new Mesh(geometry, material);
    this.rain.frustumCulled = false;
    scene.add(this.rain);

    this.splashSeeds = Float32Array.from({ length: SPLASHES }, () => rand());
    const splashes = new BufferGeometry();
    splashes.setAttribute('position', new BufferAttribute(new Float32Array(SPLASHES * 3), 3));
    splashes.setAttribute('color', new BufferAttribute(new Float32Array(SPLASHES * 3), 3));
    this.splashes = new Points(
      splashes,
      new PointsMaterial({
        size: 0.22,
        map: glow,
        vertexColors: true,
        transparent: true,
        depthWrite: false,
        blending: AdditiveBlending
      })
    );
    this.splashes.frustumCulled = false;
    scene.add(this.splashes);
    this.splashCycle = new Int32Array(SPLASHES).fill(-1);
  }

  /**
   * Advances the weather by dt to time t for a rain rate in mm/h: splashes, streaks, lightning,
   * then the light and fog of the stage for the daylight `day` and the twilight glow `warm`. Returns how stormy it looks (0..1).
   */
  tick(dt: number, t: number, rate: number, stage: Stage, day = 1, warm = 0): number {
    this.splash(t);
    this.level += (rainTarget(rate) - this.level) * (1 - Math.exp(-dt * 2));
    this.fall(dt);
    if (rate > LIGHTNING_RAIN && Math.random() < dt * 0.12) this.flash = 1;
    this.flash = Math.max(0, this.flash - dt * 5);
    const strobe = this.flash > 0 ? (Math.sin(this.flash * 40) > 0 ? this.flash : this.flash * 0.3) : 0;
    const storm = Math.min(1, this.level * 1.4);
    stage.light(storm, strobe, day, warm);
    stage.fog.density = FOG_DENSITY + storm * 0.004;
    stage.fog.color
      .set(SKY.fog)
      .lerp(STORM_FOG, storm)
      .lerp(NIGHT_FOG, (1 - day) * 0.9)
      .lerp(WARM_FOG, warm * 0.35);
    return storm;
  }

  /** Flashes splashes at random spots on the ground, as many as the rain is hard. */
  private splash(t: number): void {
    const geometry = this.splashes.geometry;
    const position = geometry.getAttribute('position');
    const color = geometry.getAttribute('color');
    const pos = position.array;
    const col = color.array;
    const r = this.box;
    const active = Math.floor(SPLASHES * Math.min(1, this.level * 1.3));
    for (let i = 0; i < SPLASHES; i++) {
      const phase = t * 2.2 + this.splashSeeds[i]! * 7;
      const cycle = Math.floor(phase);
      if (i >= active) {
        col[i * 3] = col[i * 3 + 1] = col[i * 3 + 2] = 0;
        continue;
      }
      if (this.splashCycle[i] !== cycle) {
        this.splashCycle[i] = cycle;
        const x = r.x0 + 20 + Math.random() * (r.x1 - r.x0 - 40);
        const z = r.z0 + 20 + Math.random() * (r.z1 - r.z0 - 30);
        pos[i * 3] = x;
        pos[i * 3 + 1] = 0.08;
        pos[i * 3 + 2] = z;
      }
      const b = splashBrightness(phase - cycle);
      col[i * 3] = 0.55 * b;
      col[i * 3 + 1] = 0.7 * b;
      col[i * 3 + 2] = 0.9 * b;
    }
    position.needsUpdate = true;
    color.needsUpdate = true;
  }

  /** Lets the drawn share of the streaks fall, wrapping each back to the top when it reaches the ground. */
  private fall(dt: number): void {
    const geometry = this.rain.geometry;
    const n = Math.floor(RAIN_DROPS * Math.min(1, this.level * DROPS_PER_LEVEL));
    geometry.setDrawRange(0, n * DROP_INDICES);
    if (n === 0) return;
    const position = geometry.getAttribute('position');
    const pos = position.array;
    const top = this.box.top;
    for (let i = 0; i < n; i++) {
      const o = i * DROP_VERTICES * 3;
      let y = pos[o + 1]! - this.speed[i]! * dt;
      if (y < 0) y += top;
      for (let v = 0; v < DROP_VERTICES; v++) pos[o + v * 3 + 1] = v % 4 < 2 ? y : y + STREAK;
    }
    position.needsUpdate = true;
  }
}
