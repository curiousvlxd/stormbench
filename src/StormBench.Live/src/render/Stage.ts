import {
  ACESFilmicToneMapping,
  Color,
  DirectionalLight,
  FogExp2,
  HemisphereLight,
  PCFSoftShadowMap,
  PerspectiveCamera,
  PMREMGenerator,
  Scene,
  Vector3,
  WebGLRenderer
} from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { FOG_DENSITY, SKY } from '../config';

const SUN = 2.9;
const FILM_PIXEL_RATIO = 2;
const ENVIRONMENT = 0.3;
/** What is left of each light at night, as a share of the day's. */
const NIGHT = { hemi: 0.4, sun: 0.18, environment: 0.35 } as const;
const SKY_LIGHT = { day: new Color(0xf2f6ff), night: new Color(0x6d84d8) } as const;
const GROUND_LIGHT = { day: new Color(0x7a9a55), night: new Color(0x1f2b36) } as const;
const SUN_LIGHT = { day: new Color(0xfff0d6), night: new Color(0xa9bcff), warm: new Color(0xffa865) } as const;
const MAX_PIXEL_RATIO = 2;

/** The renderer on its canvas, the scene with its fog, environment and lights, the camera and its orbit controls. */
export class Stage {
  readonly renderer: WebGLRenderer;
  readonly scene: Scene;
  readonly fog: FogExp2;
  readonly camera: PerspectiveCamera;
  readonly controls: OrbitControls;
  readonly hemi: HemisphereLight;
  readonly sun: DirectionalLight;
  readonly sunDir = new Vector3(-35, 80, 45).normalize();

  /** On `host`; for film, at least twice the page's pixels, so the recorded frames can be supersampled. */
  constructor(
    host: HTMLElement,
    private readonly film = false
  ) {
    const renderer = new WebGLRenderer({ antialias: false, powerPreference: 'high-performance' });
    renderer.setPixelRatio(this.pixelRatio());
    renderer.toneMapping = ACESFilmicToneMapping;
    renderer.toneMappingExposure = 0.95;
    renderer.shadowMap.enabled = true;
    renderer.localClippingEnabled = true;
    renderer.shadowMap.type = PCFSoftShadowMap;
    renderer.domElement.className = 'live-gl';
    host.appendChild(renderer.domElement);
    this.renderer = renderer;

    const scene = new Scene();
    this.fog = new FogExp2(SKY.fog, FOG_DENSITY);
    scene.fog = this.fog;
    const pmrem = new PMREMGenerator(renderer);
    scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    scene.environmentIntensity = ENVIRONMENT;
    pmrem.dispose();
    this.scene = scene;

    this.camera = new PerspectiveCamera(38, 1, 0.5, 1400);
    this.controls = new OrbitControls(this.camera, renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.maxPolarAngle = Math.PI * 0.46;
    this.controls.minDistance = 8;
    this.controls.maxDistance = 190;
    this.controls.autoRotateSpeed = 0.35;

    this.hemi = new HemisphereLight(0xf2f6ff, 0x7a9a55, 0.95);
    scene.add(this.hemi);
    const sun = new DirectionalLight(0xfff0d6, SUN);
    sun.position.copy(this.sunDir).multiplyScalar(140);
    sun.castShadow = true;
    sun.shadow.mapSize.set(4096, 4096);
    const sc = sun.shadow.camera;
    sc.left = -110;
    sc.right = 110;
    sc.top = 80;
    sc.bottom = -80;
    sc.near = 10;
    sc.far = 320;
    sun.shadow.bias = -0.0004;
    sun.shadow.normalBias = 0.04;
    sun.shadow.radius = 2.5;
    scene.add(sun);
    this.sun = sun;
  }

  /**
   * Sets the light for the time of day and the weather: `day` is the daylight (0 night, 1 day),
   * `warm` the glow of sunrise or sunset, `storm` (0..1) dims it all, `strobe` is a lightning
   * flash. At night the sun is the moon: cool and soft, and the sky light a deep blue.
   */
  light(storm: number, strobe: number, day = 1, warm = 0): void {
    const dark = 1 - day;
    this.hemi.intensity = (0.9 - storm * 0.3) * (NIGHT.hemi + (1 - NIGHT.hemi) * day) + strobe * 3;
    this.hemi.color.set(SKY_LIGHT.day).lerp(SKY_LIGHT.night, dark);
    this.hemi.groundColor.set(GROUND_LIGHT.day).lerp(GROUND_LIGHT.night, dark);
    this.sun.intensity = (SUN - storm * 2.1) * (NIGHT.sun + (1 - NIGHT.sun) * day);
    this.sun.color
      .set(SUN_LIGHT.day)
      .lerp(SUN_LIGHT.night, dark)
      .lerp(SUN_LIGHT.warm, warm * 0.6);
    this.scene.environmentIntensity = ENVIRONMENT * (NIGHT.environment + (1 - NIGHT.environment) * day);
  }

  /**
   * The canvas's pixels per CSS pixel: the screen's own, which zooming the page changes, up to
   * MAX_PIXEL_RATIO; for film, at least FILM_PIXEL_RATIO.
   */
  private pixelRatio(): number {
    const dpr = window.devicePixelRatio || 1;
    return this.film ? Math.max(FILM_PIXEL_RATIO, dpr) : Math.min(Math.max(dpr, 1), MAX_PIXEL_RATIO);
  }

  /** Sizes the canvas and the camera to w x h CSS pixels, at the pixel ratio of the moment. */
  resize(w: number, h: number): void {
    const pr = this.pixelRatio();
    if (pr !== this.renderer.getPixelRatio()) this.renderer.setPixelRatio(pr);
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  dispose(): void {
    this.controls.dispose();
    this.renderer.dispose();
  }
}
