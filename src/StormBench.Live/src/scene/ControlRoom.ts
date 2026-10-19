import {
  type CanvasTexture,
  MeshBasicMaterial,
  MeshStandardMaterial,
  Mesh,
  QuadraticBezierCurve3,
  Sprite,
  SpriteMaterial,
  TubeGeometry,
  Vector3
} from 'three';
import { anchorOf, meshOf, ownMaterial, place, roleOf } from '../assets/structures';
import { crossTexture } from '../assets/textures';
import { COLOR } from '../config';
import { box } from '../models/primitives';
import type { LiveFrame } from '../types';
import type { Label, Labels } from '../ui/Labels';
import { plcLabel, roomLabel } from '../ui/labelText';
import { freeSpot } from '../world/Obstacles';
import { own, type SceneContext } from './context';
import type { GateEntry } from './Gates';

interface RadioLink {
  readonly texture: CanvasTexture;
  readonly material: MeshBasicMaterial;
  readonly breakMark: Sprite;
}

const LAMP = 0xe5484d;

/**
 * The field controller (PLC) by the stations, the control room (SCADA) at the edge of town with
 * its radio mast, and the radio link arching between their aerials. The cables to the gates run
 * underground and are not drawn.
 */
export class ControlRoom {
  readonly plcAt: Vector3;
  readonly roomAt: Vector3;
  private readonly ledMaterial: MeshBasicMaterial;
  private readonly lamp: MeshStandardMaterial;
  private readonly link: RadioLink;
  private readonly plcLabel: Label;
  private readonly roomLabel: Label;
  private readonly labels: Labels;

  constructor(
    { scene, world, structures, textures, labels, picking }: SceneContext,
    gates: ReadonlyMap<string, GateEntry>
  ) {
    this.labels = labels;
    const stations = world.network.stations.map(s => gates.get(s.valve)?.group.position).filter(p => p !== undefined);
    const mean = stations
      .reduce((acc, p) => acc.add(p), new Vector3())
      .multiplyScalar(1 / Math.max(1, stations.length));
    const plcAt = freeSpot(mean.clone().add(new Vector3(0, 0, -6)), 3.5, world.pos, world.network.links);
    const cabinet = place(structures.plcCabinet);
    cabinet.position.copy(plcAt);
    this.ledMaterial = new MeshBasicMaterial({ color: COLOR.plcOk });
    const led = new Mesh(box(0.5, 0.07, 0.02), this.ledMaterial);
    led.position.copy(roleOf(cabinet, 'statusLed').position);
    cabinet.add(led);
    scene.add(cabinet);
    picking.addModel(cabinet, 'plc');
    this.plcLabel = labels.add('PLC', anchorOf(cabinet, 'label'), 0, 'plc', 'plc');

    const bounds = world.bounds;
    const roomAt = freeSpot(new Vector3(bounds.maxX - 4, 0, bounds.minZ - 2), 5.5, world.pos, world.network.links);
    const room = place(structures.controlRoom);
    room.position.copy(roomAt);
    this.lamp = ownMaterial(
      meshOf(roleOf(room, 'linkLight')),
      new MeshStandardMaterial({ color: LAMP, roughness: 0.3, emissive: LAMP, emissiveIntensity: 0 })
    );
    scene.add(room);
    picking.addModel(room, 'center');
    this.roomLabel = labels.add('SCADA', anchorOf(room, 'label'), 0, 'room', 'center');

    this.link = this.buildLink(scene, anchorOf(room, 'aerial'), anchorOf(cabinet, 'aerial'), own(textures.stripes));
    this.plcAt = plcAt;
    this.roomAt = roomAt;
  }

  /** Shows on the labels whether the link holds and whether the PLC is online or on its own. */
  update(frame: LiveFrame): void {
    this.labels.set(this.roomLabel, roomLabel(frame.link));
    this.labels.set(this.plcLabel, plcLabel(frame.plc, frame.plcLocal));
  }

  /** Runs the data along the link, blinks the broken link, the mast's obstruction lamp and the PLC's status LED. */
  tick(dt: number, t: number, frame: LiveFrame | null): void {
    const state = frame?.link ?? 'unused';
    const cut = state === 'cut' || state === 'lost';
    const link = this.link;
    link.material.color.setHex(cut ? COLOR.linkBad : state === 'up' ? COLOR.linkUp : COLOR.linkIdle);
    link.material.opacity = cut ? 0.25 + 0.25 * (Math.sin(t * 10) > 0 ? 1 : 0) : state === 'up' ? 0.9 : 0.25;
    link.texture.offset.y -= dt * (state === 'up' ? 2.2 : 0);
    link.breakMark.visible = cut;
    this.ledMaterial.color.setHex(
      frame?.plc === 'offline' ? COLOR.gateBad : frame?.plcLocal ? COLOR.plcLocal : COLOR.plcOk
    );
    this.lamp.emissiveIntensity = Math.sin(t * 3) > 0.6 ? 1.4 : 0.05;
  }

  /** The radio link: a tube arching from the dish to the PLC's aerial, with a cross that shows where it breaks. */
  private buildLink(scene: SceneContext['scene'], from: Vector3, to: Vector3, texture: CanvasTexture): RadioLink {
    const mid = from
      .clone()
      .lerp(to, 0.5)
      .setY(Math.max(from.y, to.y) + 9);
    const curve = new QuadraticBezierCurve3(from, mid, to);
    const material = new MeshBasicMaterial({
      color: COLOR.linkUp,
      alphaMap: texture,
      transparent: true,
      opacity: 0.9,
      depthWrite: false
    });
    const geometry = new TubeGeometry(curve, 120, 0.045, 6, false);
    const uv = geometry.getAttribute('uv');
    for (let i = 0; i < uv.count; i++) uv.setXY(i, uv.getY(i), uv.getX(i));
    texture.repeat.set(1, curve.getLength() / 0.9);
    scene.add(new Mesh(geometry, material));
    const breakMark = new Sprite(
      new SpriteMaterial({ map: crossTexture(), color: 0xff4d4f, transparent: true, depthWrite: false })
    );
    breakMark.position.copy(curve.getPoint(0.5));
    breakMark.scale.set(2.6, 2.6, 1);
    breakMark.visible = false;
    scene.add(breakMark);
    return { texture, material, breakMark };
  }
}
