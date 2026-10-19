import { DoubleSide, Mesh, MeshBasicMaterial, RingGeometry, type Scene, type Vector3 } from 'three';
import { COLOR } from '../config';

/** The pulsing ring on the ground around the selected structure. */
export class Selection {
  private readonly ring: Mesh<RingGeometry, MeshBasicMaterial>;

  constructor(scene: Scene) {
    this.ring = new Mesh(
      new RingGeometry(1, 1.1, 96),
      new MeshBasicMaterial({
        color: COLOR.select,
        transparent: true,
        opacity: 0.9,
        side: DoubleSide,
        depthWrite: false
      })
    );
    this.ring.rotation.x = -Math.PI / 2;
    this.ring.position.y = 0.12;
    this.ring.visible = false;
    scene.add(this.ring);
  }

  /** Rings a circle of `radius` around `at`. */
  show(at: Vector3, radius: number): void {
    this.ring.visible = true;
    this.ring.position.set(at.x, 0.12, at.z);
    this.ring.scale.setScalar(radius);
  }

  hide(): void {
    this.ring.visible = false;
  }

  tick(t: number): void {
    if (this.ring.visible) this.ring.material.opacity = 0.55 + 0.35 * Math.sin(t * 4);
  }
}
