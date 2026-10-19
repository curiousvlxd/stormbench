import { Group, type MeshStandardMaterial } from 'three';
import { anchorOf, place, roleOf } from '../assets/structures';
import type { AlarmLevel, LiveFrame } from '../types';
import type { Label, Labels } from '../ui/Labels';
import { weirLabel } from '../ui/labelText';
import { alarmLamp, pulseLamp, type SceneContext } from './context';

export interface WeirEntry {
  readonly group: Group;
  readonly lamp: MeshStandardMaterial;
  readonly label: Label;
  alarm: AlarmLevel | null;
}

/**
 * The overflow structures without a tank. The chamber with its weir lies under the street; on the
 * surface are its cover slab and the kiosk with the alarm lamp. Its spill shows at its outfall.
 */
export class Weirs {
  readonly entries = new Map<string, WeirEntry>();
  private readonly labels: Labels;

  constructor({ scene, world, structures, labels, picking }: SceneContext) {
    this.labels = labels;
    for (const id of world.network.overflowPoints) {
      if (world.tankSet.has(id)) continue;
      const p = world.pos.get(id);
      if (!p) continue;
      const group = new Group();
      group.position.copy(p).setY(0);
      group.add(place(structures.weirChamber));
      scene.add(group);
      picking.addModel(group, id);
      this.entries.set(id, {
        group,
        lamp: alarmLamp(roleOf(group, 'beacon')),
        label: labels.add(id, anchorOf(group, 'label'), 0, 'weir'),
        alarm: null
      });
    }
  }

  /** Takes the structures' alarms from a frame. */
  update(frame: LiveFrame): void {
    for (const w of frame.weirs) {
      const weir = this.entries.get(w.id);
      if (!weir) continue;
      weir.alarm = w.alarm;
      this.labels.set(weir.label, weirLabel(w));
    }
  }

  /** Pulses the alarm lamps. */
  tick(t: number): void {
    for (const weir of this.entries.values()) pulseLamp(weir.lamp, weir.alarm, t);
  }
}
