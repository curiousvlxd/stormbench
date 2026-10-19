import { Group, MeshBasicMaterial, MeshStandardMaterial, type Object3D } from 'three';
import { anchorOf, meshOf, numberOf, place, roleOf } from '../assets/structures';
import { COLOR } from '../config';
import type { AlarmLevel, LiveFrame } from '../types';
import type { Label, Labels } from '../ui/Labels';
import { gateLabel } from '../ui/labelText';
import type { SceneContext } from './context';
import type { TankEntry } from './Tanks';

interface FixedGate {
  readonly group: Group;
  readonly controlled: false;
}

export interface ControlledGate {
  readonly group: Group;
  readonly controlled: true;
  readonly base: number;
  readonly travel: number;
  readonly plate: Object3D;
  readonly plateMaterial: MeshStandardMaterial;
  readonly ghost: Object3D;
  readonly ghostMaterial: MeshBasicMaterial;
  readonly spindle: Object3D;
  readonly spindleBase: number;
  readonly tank: TankEntry;
  readonly label: Label;
  opening: number;
  target: number;
  command: number | null;
  alarm: AlarmLevel | null;
}

export type GateEntry = FixedGate | ControlledGate;

const GHOST_GAP = 0.05;

/**
 * The outlet gates of the tanks: penstocks on the tank's inner wall over the outlet, seen through
 * the ground with the tank. A controlled one has its headstock on the roof slab, its spindle rising
 * as the gate opens, and shows a wireframe outline where the PLC wants the plate; a fixed one is a
 * plate that never moves.
 */
export class Gates {
  readonly entries = new Map<string, GateEntry>();
  private readonly labels: Labels;

  constructor(
    { scene, xray, world, structures, labels, picking }: SceneContext,
    tanks: ReadonlyMap<string, TankEntry>
  ) {
    this.labels = labels;
    const controlled = new Set(world.network.stations.map(s => s.valve));
    for (const link of world.network.links) {
      const tank = tanks.get(link.from);
      if (link.kind !== 'orifice' || !tank) continue;
      const a = tank.group.position;
      const b = world.pos.get(link.to);
      if (!b) continue;
      const dir = b.clone().sub(a).setY(0).normalize();
      const at = a.clone().addScaledVector(dir, tank.shape.radius).setY(0);
      const facing = Math.atan2(-dir.x, -dir.z);
      const isControlled = controlled.has(link.id);
      const model = place(isControlled ? structures.gateControlled : structures.gateFixed);

      const inside = new Group();
      inside.position.copy(at);
      inside.rotation.y = facing;
      const section = roleOf(model, 'section');
      inside.add(section);
      const plate = roleOf(section, 'plate');
      const base = plate.position.y;
      const travel = numberOf(plate, 'travel');
      plate.position.y = base + 0.5 * travel;
      if (!isControlled) {
        xray.adopt(inside, [tank.cut]);
        tank.under.add(inside);
        inside.position.sub(tank.under.position);
        this.entries.set(link.id, { group: inside, controlled: false });
        continue;
      }
      meshOf(plate).material = new MeshStandardMaterial({
        color: COLOR.gateOk,
        roughness: 0.4,
        metalness: 0.55,
        emissive: 0x000000
      });
      const ghost = plate.clone(true);
      meshOf(ghost).material = new MeshBasicMaterial({
        color: 0xffffff,
        wireframe: true,
        transparent: true,
        opacity: 0
      });
      plate.parent?.add(ghost);
      xray.adopt(inside, [tank.cut]);
      tank.under.add(inside);
      inside.position.sub(tank.under.position);

      const group = new Group();
      group.position.copy(at);
      group.rotation.y = facing;
      group.add(roleOf(model, 'surface'));
      scene.add(group);
      const spindle = roleOf(group, 'spindle');
      const spindleBase = spindle.position.y;
      picking.addModel(group, link.id);
      this.entries.set(link.id, {
        group,
        controlled: true,
        base,
        travel,
        plate,
        plateMaterial: meshOf(plate).material as MeshStandardMaterial,
        ghost,
        ghostMaterial: meshOf(ghost).material as MeshBasicMaterial,
        spindle,
        spindleBase,
        tank,
        label: labels.add(link.id, anchorOf(group, 'label'), 0, 'gate'),
        opening: 0.5,
        target: 0.5,
        command: null,
        alarm: null
      });
    }
  }

  /** Takes the gates' positions, commands and alarms from a frame. */
  update(frame: LiveFrame): void {
    for (const g of frame.gates) {
      const gate = this.entries.get(g.id);
      if (!gate?.controlled) continue;
      gate.target = g.opening ?? g.setpoint ?? gate.target;
      gate.command = g.setpoint;
      gate.alarm = g.alarm;
      this.labels.set(gate.label, gateLabel(g));
    }
  }

  /**
   * Eases each plate, and its spindle on the surface, towards the gate's position; a jammed gate
   * glows red, a command it has not reached shows as the outline.
   */
  tick(t: number, k: number): void {
    for (const gate of this.entries.values()) {
      if (!gate.controlled) continue;
      gate.opening += (gate.target - gate.opening) * k;
      gate.plate.position.y = gate.base + gate.opening * gate.travel;
      gate.spindle.position.y = gate.spindleBase + gate.opening * gate.travel;
      const bad = !!gate.alarm;
      gate.plateMaterial.color.setHex(bad ? COLOR.gateBad : COLOR.gateOk);
      gate.plateMaterial.emissive.setHex(bad ? COLOR.gateBad : 0x000000);
      gate.plateMaterial.emissiveIntensity = bad ? 0.5 + 0.4 * Math.sin(t * 7) : 0;
      if (gate.command != null && Math.abs(gate.command - gate.opening) > GHOST_GAP) {
        gate.ghostMaterial.opacity = 0.7;
        gate.ghost.position.y = gate.base + gate.command * gate.travel;
      } else {
        gate.ghostMaterial.opacity = 0;
      }
    }
  }
}
