import { MathUtils, type PerspectiveCamera, Vector3 } from 'three';
import type { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { type Bounds, middle } from '../world/World';

export interface Tween {
  t: number;
  readonly fromTarget: Vector3;
  readonly toTarget: Vector3;
  readonly fromPos: Vector3;
  readonly toPos: Vector3;
  readonly slow?: boolean;
}

export interface Anchor {
  readonly p: Vector3;
  readonly r: number;
  readonly d: number;
}

const IDLE_MS = 20000;
/** Where the overview looks from, per unit of the town's span. */
const OVERVIEW = new Vector3(-0.02, 0.7, 0.62);
/**
 * How far the straight-down shot leans towards the overview, in metres: enough to fix which way
 * is up on screen, so the picture neither creeps round in the hold nor turns when the reveal starts.
 */
const TOP_DOWN_LEAN = 0.4;
const SLOW_S = 9;
const FILM_S = 3.4;
const QUICK_S = 1.1;

/** Smooth in and out over 0..1 (cubic). */
export function easeInOut(t: number): number {
  return t < 0.5 ? 4 * t ** 3 : 1 - (-2 * t + 2) ** 3 / 2;
}

/**
 * Where the camera looks: flights between views, the overview of the whole town, the close view
 * of a picked structure, the film shots, and the slow turn when nobody touches the view.
 */
export class CameraDirector {
  tween: Tween | null = null;
  filmHold = false;
  private lastInteraction = performance.now();

  constructor(
    private readonly camera: PerspectiveCamera,
    private readonly controls: OrbitControls,
    private readonly film: boolean
  ) {
    controls.addEventListener('start', () => {
      this.touch();
      this.tween = null;
    });
  }

  /** Someone used the view: the idle turn waits again. */
  touch(): void {
    this.lastInteraction = performance.now();
  }

  /** Flies the camera to look at `target` from `position`. */
  flyTo(target: Vector3, position: Vector3): void {
    this.tween = {
      t: 0,
      fromTarget: this.controls.target.clone(),
      toTarget: target,
      fromPos: this.camera.position.clone(),
      toPos: position
    };
    this.touch();
  }

  /** The whole town from the south, high enough that it all fits. */
  overview(bounds: Bounds, instant = false): void {
    const b = bounds;
    const target = middle(b).add(new Vector3(0, 0, 1));
    const span = Math.max(b.maxX - b.minX, (b.maxZ - b.minZ) * 1.7);
    const position = target.clone().add(OVERVIEW.clone().multiplyScalar(span));
    if (instant) {
      this.controls.target.copy(target);
      this.camera.position.copy(position);
      this.controls.update();
    } else {
      this.flyTo(target, position);
    }
  }

  /** A closer look at an anchor, from the side the camera already looks from. */
  focus(anchor: Anchor): void {
    const target = anchor.p.clone().setY(1.5);
    const offset = this.camera.position.clone().sub(this.controls.target).normalize().multiplyScalar(anchor.d);
    offset.y = Math.max(offset.y, anchor.d * 0.65);
    this.flyTo(target, target.clone().add(offset));
  }

  /** Film: start low over the river, then rise slowly to the whole town. */
  opening(low: Vector3, bounds: Bounds): void {
    const target = middle(bounds);
    this.controls.target.copy(low).setY(1);
    this.camera.position.copy(low).add(new Vector3(-18, 6, 16));
    this.controls.update(0);
    this.tween = {
      t: 0,
      fromTarget: this.controls.target.clone(),
      toTarget: target,
      fromPos: this.camera.position.clone(),
      toPos: target.clone().add(new Vector3(-2, 84, 74)),
      slow: true
    };
  }

  /** Film: straight down on the town, held until the reveal. */
  topDown(bounds: Bounds): void {
    const b = bounds;
    const t = middle(b);
    const half = Math.tan(MathUtils.degToRad(this.camera.fov / 2));
    const h = Math.max((b.maxZ - b.minZ) / 2 / half, (b.maxX - b.minX) / 2 / (half * this.camera.aspect)) * 1.12;
    this.tween = null;
    this.filmHold = true;
    this.settle();
    this.controls.target.copy(t);
    const lean = new Vector3(OVERVIEW.x, 0, OVERVIEW.z).setLength(TOP_DOWN_LEAN);
    this.camera.position
      .copy(t)
      .add(new Vector3(0, h, 0))
      .add(lean);
    this.controls.update(0);
  }

  /** Drops whatever turn or zoom the controls are still damping out, so a held shot stays put. */
  private settle(): void {
    const damping = this.controls.enableDamping;
    this.controls.autoRotate = false;
    this.controls.enableDamping = false;
    this.controls.update(0);
    this.controls.enableDamping = damping;
  }

  /** Advances a flight and the idle turn by dt seconds; `running` stops the turn while a storm runs. */
  tick(dt: number, running: boolean): void {
    const tw = this.tween;
    if (tw) {
      tw.t = Math.min(1, tw.t + dt / (tw.slow ? SLOW_S : this.film ? FILM_S : QUICK_S));
      const e = easeInOut(tw.t);
      this.controls.target.lerpVectors(tw.fromTarget, tw.toTarget, e);
      this.camera.position.lerpVectors(tw.fromPos, tw.toPos, e);
      if (tw.t >= 1) this.tween = null;
    }
    if (this.film) {
      this.controls.autoRotate = !this.tween && !this.filmHold;
      this.controls.autoRotateSpeed = 0.45;
    } else {
      this.controls.autoRotate = !running && !this.tween && performance.now() - this.lastInteraction > IDLE_MS;
    }
    this.controls.update(dt);
  }
}
