import {
  BackSide,
  Box3,
  type BufferGeometry,
  CircleGeometry,
  CylinderGeometry,
  type Camera,
  Group,
  Mesh,
  MeshBasicMaterial,
  MeshStandardMaterial,
  type Object3D,
  Plane,
  PlaneGeometry,
  TorusGeometry,
  Vector3
} from 'three';
import { flat } from '../assets/materials';
import { TANK_LENS } from '../config';
import { anchorOf, meshOf, numberOf, ownMaterial, place, roleOf } from '../assets/structures';
import { box, merge } from '../models/primitives';
import { Xray } from '../render/Xray';
import type { AlarmLevel, LiveFrame } from '../types';
import type { Label, Labels } from '../ui/Labels';
import { tankLabel } from '../ui/labelText';
import { alarmLamp, pulseLamp, type SceneContext } from './context';

/** The inside of a tank, from the model: radius, clear height, roof and floor. */
export interface TankShape {
  readonly radius: number;
  readonly height: number;
  readonly roof: number;
}

export interface TankEntry {
  readonly group: Group;
  readonly lens: Object3D;
  readonly under: Object3D;
  readonly section: Group;
  readonly cut: Plane;
  readonly near: Plane;
  readonly shape: TankShape;
  readonly water: Mesh;
  readonly waterMaterial: MeshStandardMaterial;
  readonly top: Mesh;
  readonly topMaterial: MeshStandardMaterial;
  readonly sensorLine: Mesh;
  readonly sensorMaterial: MeshStandardMaterial;
  readonly lamp: MeshStandardMaterial;
  readonly label: Label;
  fill: number;
  target: number;
  sensorFill: number | null;
  overflowing: boolean;
  alarm: AlarmLevel | null;
}

const WATER = 0x2a72b8;
const SURFACE = 0x3b9be0;
const GLOW = 0x0b3c74;
const TICK = 0x2b3440;
const SHAFT = 0x5c5853;
const SHAFT_DEPTH = 8;
const THRESHOLD = 0xf39a2b;
const SHELL = 0xdfe4ea;
const SHELL_FACE = 0.16;
const SHELL_EDGE = 0.62;
const RIM = 0.035;

/**
 * The concrete of the half of a tank the cut takes away, as glass: faint where it faces the
 * camera, stronger towards its outline, so the tank keeps its whole shape and what is on its roof
 * stands on something, while the cut stays in view through it.
 */
function shellMaterial(xray: Xray, near: Plane): MeshStandardMaterial {
  const material = xray.through(
    new MeshStandardMaterial({ color: SHELL, roughness: 0.3, metalness: 0, transparent: true, depthWrite: false }),
    [near]
  );
  material.onBeforeCompile = shader => {
    shader.fragmentShader = shader.fragmentShader.replace(
      '#include <opaque_fragment>',
      `diffuseColor.a = mix(${SHELL_EDGE.toFixed(2)}, ${SHELL_FACE.toFixed(2)}, abs(dot(normalize(normal), normalize(vViewPosition))));
#include <opaque_fragment>`
    );
  };
  return material;
}

/** Which way an outlet leaves the tank at p, as a heading about y (0 is +z), or null if it has none. */
function outletHeading({ world }: SceneContext, id: string, p: Vector3): number | null {
  const link = world.network.links.find(l => l.kind === 'orifice' && l.from === id);
  const to = link ? world.pos.get(link.to) : undefined;
  return to ? Math.atan2(to.x - p.x, to.z - p.z) : null;
}

/**
 * The marks on the staff gauge on the far wall: a tick per metre of the benchmark's tank depth,
 * a longer orange one at `thresholdM`.
 */
function staffMarks(staff: Object3D, shape: TankShape, depthM: number, thresholdM: number): BufferGeometry[] {
  const bottom = numberOf(staff, 'staffBottom');
  const top = numberOf(staff, 'staffTop');
  const z = new Box3().setFromObject(staff).max.z + 0.006;
  const floor = -(shape.roof + shape.height);
  const ticks: BufferGeometry[] = [];
  const threshold: BufferGeometry[] = [];
  for (let m = 0; m <= depthM; m++) {
    const y = floor + (m / depthM) * shape.height;
    if (y < bottom - 0.01 || y > top + 0.01) continue;
    const long = m === thresholdM;
    (long ? threshold : ticks).push(box(long ? 0.2 : 0.11, 0.035, 0.012, 0, y, z));
  }
  return [merge(ticks), merge(threshold)];
}

/**
 * The storage tanks lie under the ground, as they do in a town. On the surface: the roof slab
 * with its hatch, vent and the kiosk with the alarm lamp. Under it, always open to the
 * x-ray, seen through a lens in the ground, down a shaft of soil that hides every other tank: the
 * tank cut in half across the line of sight, with the soil around it, the water at its live level
 * against a staff gauge on the far wall, and the frozen sensor's reading as a line when it lies.
 * The half the cut takes away stays as glass, so the tank is whole and its roof keeps all it carries.
 */
export class Tanks {
  readonly entries = new Map<string, TankEntry>();
  private readonly labels: Labels;
  private readonly toCamera = new Vector3();

  constructor(ctx: SceneContext) {
    const { scene, xray, world, structures, labels, picking, surfaces } = ctx;
    this.labels = labels;
    const sectionModel = roleOf(structures.tank, 'section');
    const shape: TankShape = {
      radius: numberOf(sectionModel, 'radius'),
      height: numberOf(sectionModel, 'height'),
      roof: numberOf(sectionModel, 'roof')
    };
    const R = shape.radius;
    const floor = -(shape.roof + shape.height);
    const waterBody = new PlaneGeometry(2 * (R - 0.01), 1);
    waterBody.translate(0, 0.5, 0.002);
    const surface = surfaces.disc(R - 0.01);
    const shaftWall = new CylinderGeometry(TANK_LENS, TANK_LENS, SHAFT_DEPTH, 48, 1, true);
    const lidDisc = new CircleGeometry(TANK_LENS + 0.05, 48, 0, Math.PI);
    lidDisc.rotateX(-Math.PI / 2);
    const outer = R + numberOf(sectionModel, 'thickness');
    const depth = shape.roof + shape.height + numberOf(sectionModel, 'floor');
    const shellWall = new CylinderGeometry(outer, outer, depth, 64, 1, true);
    shellWall.translate(0, -depth / 2, 0);
    const shellRoof = new CircleGeometry(outer, 64);
    shellRoof.rotateX(-Math.PI / 2);
    const rimRing = new TorusGeometry(outer, RIM, 6, 96);
    rimRing.rotateX(-Math.PI / 2);
    rimRing.translate(0, -RIM, 0);

    for (const id of world.network.tanks) {
      const p = world.pos.get(id);
      if (!p) continue;
      const model = place(structures.tank);
      const top = roleOf(model, 'surface');
      const under = roleOf(model, 'section');

      const group = new Group();
      group.position.copy(p).setY(0);
      group.add(top);
      const heading = outletHeading(ctx, id, p);
      if (heading !== null) group.rotation.y = heading - numberOf(top, 'gateBearing');
      scene.add(group);
      group.updateWorldMatrix(true, true);
      const lamp = alarmLamp(roleOf(top, 'beacon'));

      const cut = new Plane();
      const near = new Plane();
      const root = new Group();
      root.position.copy(p).setY(0);
      const section = new Group();
      section.add(under);
      root.add(section);
      ownMaterial(
        meshOf(roleOf(under, 'sensor')),
        new MeshStandardMaterial({
          color: 0xd9dde2,
          roughness: 0.4,
          metalness: 0.5,
          emissive: 0xffa23a,
          emissiveIntensity: 0
        })
      );
      const [ticks, threshold] = staffMarks(roleOf(under, 'staff'), shape, world.network.tankMaxDepthM, 1);
      section.add(new Mesh(ticks, flat(TICK, { roughness: 0.8 })));
      section.add(new Mesh(threshold, flat(THRESHOLD, { roughness: 0.6 })));

      const waterMaterial = new MeshStandardMaterial({
        color: WATER,
        roughness: 0.25,
        metalness: 0,
        emissive: 0x08305c,
        emissiveIntensity: 0.35
      });
      const water = new Mesh(waterBody, waterMaterial);
      water.position.y = floor;
      water.scale.y = 0.001;
      section.add(water);
      const sensorLine = new Mesh(
        new TorusGeometry(R - 0.02, 0.04, 6, 48, Math.PI),
        new MeshBasicMaterial({ color: 0xffa23a })
      );
      sensorLine.rotation.x = -Math.PI / 2;
      sensorLine.visible = false;
      section.add(sensorLine);
      const shaft = new Mesh(shaftWall, flat(SHAFT, { roughness: 1, side: BackSide }));
      shaft.position.y = -SHAFT_DEPTH / 2;
      section.add(shaft);
      xray.adopt(section);
      meshOf(roleOf(under, 'lid')).visible = false;
      const lid = new Mesh(lidDisc);
      Xray.lid(lid);
      section.add(lid);
      const lens = Xray.lens(p, TANK_LENS);
      scene.add(lens);

      const topMaterial = xray.through(
        flat(SURFACE, { roughness: 0.35, metalness: 0.05, emissive: GLOW, emissiveIntensity: 0.25 }),
        [cut]
      );
      const waterTop = new Mesh(surface, topMaterial);
      waterTop.position.y = floor;
      root.add(waterTop);
      const shell = shellMaterial(xray, near);
      for (const geometry of [shellWall, shellRoof]) {
        const glass = new Mesh(geometry, shell);
        glass.renderOrder = 2;
        root.add(glass);
      }
      const rim = new Mesh(rimRing, xray.through(flat(SHELL, { roughness: 0.6 }), [near]));
      root.add(rim);
      xray.scene.add(root);

      picking.addModel(group, id);
      this.entries.set(id, {
        group,
        lens,
        under: root,
        section,
        cut,
        near,
        shape,
        water,
        waterMaterial: water.material as MeshStandardMaterial,
        top: waterTop,
        topMaterial,
        sensorLine,
        sensorMaterial: meshOf(roleOf(under, 'sensor')).material as MeshStandardMaterial,
        lamp,
        label: labels.add(id, anchorOf(group, 'label'), 0, 'tank'),
        fill: 0,
        target: 0,
        sensorFill: null,
        overflowing: false,
        alarm: null
      });
    }
  }

  /** Takes the tanks' levels, spills and alarms from a frame. */
  update(frame: LiveFrame): void {
    for (const t of frame.tanks) {
      const tank = this.entries.get(t.id);
      if (!tank) continue;
      tank.target = t.trueFill ?? t.fill ?? 0;
      tank.sensorFill = t.trueFill != null ? t.fill : null;
      tank.overflowing = t.overflowing;
      tank.alarm = t.alarm;
      this.labels.set(tank.label, tankLabel(t));
    }
  }

  /**
   * Turns each tank's cut to face the camera, eases its water towards its level, and animates the
   * sensor and the alarm lamp.
   */
  tick(t: number, k: number, camera: Camera): void {
    for (const tank of this.entries.values()) {
      const p = tank.group.position;
      this.toCamera.copy(camera.position).sub(p).setY(0);
      if (this.toCamera.lengthSq() < 1e-6) this.toCamera.set(0, 0, 1);
      this.toCamera.normalize();
      tank.section.rotation.y = Math.atan2(this.toCamera.x, this.toCamera.z);
      tank.cut.setFromNormalAndCoplanarPoint(this.toCamera.clone().negate(), p);
      tank.near.copy(tank.cut).negate();

      const { height, roof } = tank.shape;
      const floor = -(roof + height);
      tank.fill += (tank.target - tank.fill) * k;
      const h = Math.max(0.001, tank.fill * height);
      tank.water.scale.y = h;
      tank.water.visible = tank.fill > 0.004;
      tank.top.position.y = floor + h + 0.004;
      tank.top.visible = tank.fill > 0.004;
      if (tank.sensorFill != null) {
        tank.sensorLine.visible = true;
        tank.sensorLine.position.y = floor + Math.max(0.03, tank.sensorFill * height);
        tank.sensorMaterial.emissiveIntensity = 0.6 + 0.4 * Math.sin(t * 6);
      } else {
        tank.sensorLine.visible = false;
        tank.sensorMaterial.emissiveIntensity = 0;
      }
      pulseLamp(tank.lamp, tank.alarm, t);
    }
  }
}
