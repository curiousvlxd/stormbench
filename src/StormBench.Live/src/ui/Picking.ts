import { Mesh, type Object3D, type PerspectiveCamera, Raycaster, Vector2 } from 'three';

const CLICK_SLOP = 5;

/** Clicks on the scene: a press and release in the same spot picks the structure under the pointer, or nothing. */
export class Picking {
  private readonly targets: Object3D[] = [];
  private down: { x: number; y: number } | null = null;

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly camera: PerspectiveCamera,
    private readonly onPick: (id: string | null) => void,
    private readonly onTouch: () => void
  ) {
    canvas.addEventListener('pointerdown', this.press);
    canvas.addEventListener('pointerup', this.release);
  }

  /** Makes `mesh` stand for `id` when clicked. */
  add(mesh: Object3D, id: string): void {
    mesh.userData['pick'] = id;
    this.targets.push(mesh);
  }

  /** Makes every mesh of a model stand for `id`, so a click on what is drawn picks it. */
  addModel(root: Object3D, id: string): void {
    root.traverse(o => {
      if (o instanceof Mesh) this.add(o, id);
    });
  }

  dispose(): void {
    this.canvas.removeEventListener('pointerdown', this.press);
    this.canvas.removeEventListener('pointerup', this.release);
  }

  private readonly press = (e: PointerEvent): void => {
    this.down = { x: e.clientX, y: e.clientY };
    this.onTouch();
  };

  private readonly release = (e: PointerEvent): void => {
    const down = this.down;
    if (!down || Math.hypot(e.clientX - down.x, e.clientY - down.y) > CLICK_SLOP) return;
    const rect = this.canvas.getBoundingClientRect();
    const ndc = new Vector2(
      ((e.clientX - rect.left) / rect.width) * 2 - 1,
      -((e.clientY - rect.top) / rect.height) * 2 + 1
    );
    const ray = new Raycaster();
    ray.setFromCamera(ndc, this.camera);
    const hit = ray.intersectObjects(this.targets, false)[0];
    this.onPick(hit ? (hit.object.userData['pick'] as string) : null);
  };
}
