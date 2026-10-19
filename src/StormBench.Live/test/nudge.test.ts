import { Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { PLANT_MIDDLE, PLANT_RADIUS, PLANT_SITE, TANK_R } from '../src/config';
import {
  clearChambersOffTanks,
  clearPlantOffWater,
  clearStructuresOffWater,
  moveJunctionsOffWater,
  neighbours,
  spreadAlongBank
} from '../src/world/nudge';
import { nearestWater, waterPath } from '../src/world/water';

const river = () => [waterPath('River', [new Vector3(-60, 0, 0), new Vector3(60, 0, 0)], 6)];

describe('clearChambersOffTanks', () => {
  it('moves a chamber out of a tank to a clear ring, away from the tank', () => {
    const pos = new Map([
      ['T1', new Vector3(0, 0, 0)],
      ['CSO1', new Vector3(1, 0, 0)],
      ['CSO2', new Vector3(20, 0, 0)]
    ]);
    clearChambersOffTanks(pos, ['T1'], ['CSO1', 'CSO2']);
    expect(pos.get('CSO1')!.x).toBeCloseTo(TANK_R + 3.6);
    expect(pos.get('CSO1')!.z).toBeCloseTo(0);
    expect(pos.get('CSO2')!.x).toBe(20);
  });

  it('picks a direction when the chamber sits on the tank centre', () => {
    const pos = new Map([
      ['T1', new Vector3(5, 0, 5)],
      ['CSO1', new Vector3(5, 0, 5)]
    ]);
    clearChambersOffTanks(pos, ['T1'], ['CSO1']);
    expect(pos.get('CSO1')!.distanceTo(pos.get('T1')!)).toBeCloseTo(TANK_R + 3.6);
  });
});

describe('clearStructuresOffWater', () => {
  it('steps structures out of the water until their footprint is dry', () => {
    const waters = river();
    const pos = new Map([
      ['T1', new Vector3(10, 0, 1)],
      ['CSO1', new Vector3(-10, 0, -2)],
      ['T2', new Vector3(0, 0, 30)]
    ]);
    const size = (id: string) => (id.startsWith('T') ? TANK_R + 1 : 2);
    clearStructuresOffWater(pos, ['T1', 'CSO1', 'T2'], size, waters);
    expect(nearestWater(pos.get('T1')!, waters).distance).toBeGreaterThanOrEqual(TANK_R + 1);
    expect(pos.get('T1')!.z).toBeGreaterThan(0);
    expect(nearestWater(pos.get('CSO1')!, waters).distance).toBeGreaterThanOrEqual(2);
    expect(pos.get('CSO1')!.z).toBeLessThan(0);
    expect(pos.get('T2')!.toArray()).toEqual([0, 0, 30]);
  });
});

describe('moveJunctionsOffWater', () => {
  it('moves manholes in the water onto the bank their pipes come from', () => {
    const waters = river();
    const pos = new Map([
      ['J1', new Vector3(0, 0, 0.5)],
      ['J2', new Vector3(0, 0, -20)],
      ['T1', new Vector3(5, 0, 1)]
    ]);
    const links = [{ id: 'C1', kind: 'conduit', from: 'J2', to: 'J1' }];
    const moved = moveJunctionsOffWater(pos, links, new Set(['T1']), waters);
    expect([...moved]).toEqual(['J1']);
    expect(pos.get('J1')!.z).toBeCloseTo(-(3 + 1.3));
    expect(pos.get('T1')!.z).toBe(1);
  });

  it('keeps a manhole on its own side when nothing pulls it', () => {
    const waters = river();
    const pos = new Map([['J1', new Vector3(3, 0, 2)]]);
    moveJunctionsOffWater(pos, [], new Set(), waters);
    expect(pos.get('J1')!.z).toBeCloseTo(3 + 1.3);
  });
});

describe('spreadAlongBank', () => {
  it('pulls moved manholes that landed together apart along the bank', () => {
    const waters = river();
    const pos = new Map([
      ['A', new Vector3(0, 0, 4.3)],
      ['B', new Vector3(0.2, 0, 4.3)]
    ]);
    spreadAlongBank(pos, new Set(['A', 'B']), waters);
    const a = pos.get('A')!;
    const b = pos.get('B')!;
    expect(Math.abs(b.x - a.x)).toBeCloseTo(1.7);
    expect(a.z).toBeCloseTo(4.3);
  });
});

describe('neighbours', () => {
  it('lists the positions linked either way', () => {
    const pos = new Map([
      ['A', new Vector3(0, 0, 0)],
      ['B', new Vector3(1, 0, 0)],
      ['C', new Vector3(2, 0, 0)]
    ]);
    const links = [
      { id: '1', kind: 'conduit', from: 'A', to: 'B' },
      { id: '2', kind: 'conduit', from: 'C', to: 'A' },
      { id: '3', kind: 'conduit', from: 'A', to: 'X' }
    ];
    expect(neighbours(pos, links, 'A').map(q => q.x)).toEqual([1, 2]);
  });
});

describe('clearPlantOffWater', () => {
  it('steps the plant back until its yard is clear of the water', () => {
    const waters = river();
    const outfall = new Vector3(0, 0, 2);
    clearPlantOffWater(outfall, waters);
    const centre = outfall.clone().add(new Vector3(PLANT_SITE.x + PLANT_MIDDLE.x, 0, PLANT_SITE.z + PLANT_MIDDLE.z));
    expect(nearestWater(centre, waters).distance).toBeGreaterThanOrEqual(PLANT_RADIUS);
  });
});
