import {
  BackSide,
  Color,
  Group,
  IcosahedronGeometry,
  Mesh,
  type MeshStandardMaterial,
  type Scene,
  ShaderMaterial,
  SphereGeometry
} from 'three';
import { flat } from '../assets/materials';
import { SKY } from '../config';
import { skyFragment, skyVertex } from '../shaders/sky';
import { type Bounds, middle } from '../world/World';

const CLOUDS = 26;
const PUFFS = 5;

/** The sky dome that greys over in a storm and darkens at night, and the low-poly clouds drifting round the town. */
export class Sky {
  private readonly uniforms = {
    uZenith: { value: new Color(SKY.zenith) },
    uHorizon: { value: new Color(SKY.horizon) },
    uStorm: { value: 0 },
    uNightZenith: { value: new Color(SKY.nightZenith) },
    uNightHorizon: { value: new Color(SKY.nightHorizon) },
    uWarmHorizon: { value: new Color(SKY.warmHorizon) },
    uNight: { value: 0 },
    uWarm: { value: 0 }
  };
  private readonly cloudMaterial: MeshStandardMaterial;
  private readonly clouds = new Group();

  constructor(scene: Scene, bounds: Bounds, rand: () => number) {
    const dome = new Mesh(
      new SphereGeometry(600, 32, 16),
      new ShaderMaterial({
        uniforms: this.uniforms,
        vertexShader: skyVertex,
        fragmentShader: skyFragment,
        side: BackSide,
        depthWrite: false,
        fog: false
      })
    );
    scene.add(dome);
    const puff = new IcosahedronGeometry(1, 1);
    this.cloudMaterial = flat(0xffffff, { roughness: 1, transparent: true, opacity: 0.92 });
    const centre = middle(bounds);
    for (let i = 0; i < CLOUDS; i++) {
      const cloud = new Group();
      for (let k = 0; k < PUFFS; k++) {
        const m = new Mesh(puff, this.cloudMaterial);
        const s = 2.2 + rand() * 2.6;
        m.scale.set(s * 1.4, s * 0.8, s);
        m.position.set((k - 2) * 2.4 + rand(), rand() * 1.2, rand() * 2 - 1);
        cloud.add(m);
      }
      const a = rand() * Math.PI * 2;
      const r = 150 + rand() * 80;
      cloud.position.set(centre.x + Math.cos(a) * r, 26 + rand() * 18, centre.z + Math.sin(a) * r);
      cloud.scale.setScalar(2.2 + rand() * 1.6);
      cloud.userData['speed'] = 0.4 + rand() * 0.6;
      this.clouds.add(cloud);
    }
    scene.add(this.clouds);
  }

  /**
   * Greys the sky and the clouds as the storm (0..1) builds and lets the clouds drift faster;
   * darkens both into the night as the daylight `day` goes, warm at sunrise and sunset (`warm`).
   */
  tick(dt: number, storm: number, day = 1, warm = 0): void {
    this.uniforms.uStorm.value = storm;
    this.uniforms.uNight.value = 1 - day;
    this.uniforms.uWarm.value = warm;
    const light = 0.3 + 0.7 * day;
    this.cloudMaterial.color.setRGB(
      (1 - storm * 0.55) * light * (1 + warm * 0.08),
      (1 - storm * 0.52) * light,
      (1 - storm * 0.48) * light * (1 + (1 - day) * 0.15)
    );
    this.clouds.rotation.y += dt * 0.004 * (1 + storm * 3);
  }
}
