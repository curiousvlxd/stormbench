import {
  AlwaysStencilFunc,
  type Camera,
  CircleGeometry,
  DirectionalLight,
  DoubleSide,
  Group,
  EqualStencilFunc,
  type FogExp2,
  HemisphereLight,
  type Material,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  type Object3D,
  type Plane,
  ReplaceStencilOp,
  RingGeometry,
  Scene,
  type Texture,
  type Vector3,
  type WebGLRenderer,
  type WebGLRenderTarget
} from 'three';
import { Pass } from 'three/examples/jsm/postprocessing/Pass.js';

const LENS = 1;
const LENS_Y = 0.05;
const RIM = 0.12;
const RIM_COLOR = 0x4a5361;
const HEMI = 1.1;
const SUN = 2.2;
const ENVIRONMENT = 0.3;
/** What is left of the light under the ground at night, as a share of the day's. */
const NIGHT = 0.42;

function materialsOf(o: Object3D): Material[] {
  const m = (o as Object3D & { material?: Material | Material[] }).material;
  return m ? (Array.isArray(m) ? m : [m]) : [];
}

/**
 * What lies under the ground (the tanks and the gates in them), in a scene of its own. The town
 * marks lenses on the ground: discs over each tank, drawn last and only where nothing stands in
 * front of them. The x-ray is drawn after the town, with a fresh depth buffer, only inside the
 * lenses: seen through the ground, never through a house, a tree or a kiosk in front.
 */
export class Xray {
  readonly scene = new Scene();
  private readonly copies = new Map<Material, Material>();
  private readonly hemi = new HemisphereLight(0xf2f6ff, 0x6b5a48, HEMI);
  private readonly sun = new DirectionalLight(0xfff0d6, SUN);

  constructor(fog: FogExp2, sunDir: Vector3, environment: Texture | null) {
    this.scene.fog = fog;
    this.scene.environment = environment;
    this.scene.environmentIntensity = ENVIRONMENT;
    this.scene.add(this.hemi);
    this.sun.position.copy(sunDir).multiplyScalar(140);
    this.scene.add(this.sun);
  }

  /** Dims what is seen under the ground with the town's light: `day` 1 by day, 0 at night. */
  light(day: number): void {
    const share = NIGHT + (1 - NIGHT) * day;
    this.hemi.intensity = HEMI * share;
    this.sun.intensity = SUN * share;
    this.scene.environmentIntensity = ENVIRONMENT * share;
  }

  /** Moves a model under the ground: every material becomes a copy that draws only through the ground, cut by `clipping`. */
  adopt(root: Object3D, clipping: readonly Plane[] = []): void {
    root.traverse(o => {
      if (!(o instanceof Mesh)) return;
      o.castShadow = false;
      o.receiveShadow = false;
      const materials = materialsOf(o);
      const copies = materials.map(m => this.through(m, clipping));
      o.material = Array.isArray(o.material) ? copies : copies[0]!;
    });
  }

  /** A material for something under the ground: drawn only where the town shows ground, cut by `clipping`. */
  through<M extends Material>(material: M, clipping: readonly Plane[] = []): M {
    const known = clipping.length ? null : this.copies.get(material);
    if (known) return known as M;
    const copy = material.clone() as M;
    copy.stencilWrite = true;
    copy.stencilRef = LENS;
    copy.stencilFunc = EqualStencilFunc;
    copy.stencilWriteMask = 0;
    if (clipping.length) copy.clippingPlanes = [...clipping];
    if (!clipping.length) this.copies.set(material, copy);
    return copy;
  }

  /** A lens of `radius` on the ground at `at`, for the town's scene, with a thin dark rim that frames it. */
  static lens(at: Vector3, radius: number): Group {
    const group = new Group();
    group.position.copy(at).setY(0);
    const disc = new CircleGeometry(radius, 64);
    disc.rotateX(-Math.PI / 2);
    const lens = new Mesh(
      disc,
      new MeshBasicMaterial({
        colorWrite: false,
        depthWrite: false,
        transparent: true,
        stencilWrite: true,
        stencilRef: LENS,
        stencilFunc: AlwaysStencilFunc,
        stencilZPass: ReplaceStencilOp
      })
    );
    lens.position.y = LENS_Y;
    lens.renderOrder = 10;
    group.add(lens);
    const ring = new RingGeometry(radius, radius + RIM, 64);
    ring.rotateX(-Math.PI / 2);
    const rim = new Mesh(ring, new MeshStandardMaterial({ color: RIM_COLOR, roughness: 0.8 }));
    rim.position.y = LENS_Y - 0.01;
    rim.receiveShadow = true;
    group.add(rim);
    return group;
  }

  /** A surface that only hides what lies under it: the ground that is not cut away. */
  static lid(mesh: Mesh): void {
    mesh.material = new MeshBasicMaterial({
      colorWrite: false,
      side: DoubleSide,
      stencilWrite: true,
      stencilRef: LENS,
      stencilFunc: EqualStencilFunc,
      stencilWriteMask: 0
    });
    mesh.castShadow = false;
    mesh.receiveShadow = false;
    mesh.renderOrder = -1;
  }
}

/** The pass that draws the x-ray scene into the frame the town was just drawn in, keeping its stencil. */
export class XrayPass extends Pass {
  constructor(
    private readonly xray: Xray,
    private readonly camera: Camera
  ) {
    super();
    this.needsSwap = false;
  }

  override render(renderer: WebGLRenderer, _write: WebGLRenderTarget, read: WebGLRenderTarget): void {
    const autoClear = renderer.autoClear;
    renderer.autoClear = false;
    renderer.setRenderTarget(this.renderToScreen ? null : read);
    renderer.clearDepth();
    renderer.render(this.xray.scene, this.camera);
    renderer.autoClear = autoClear;
  }
}
