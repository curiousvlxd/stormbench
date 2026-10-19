import { describe, expect, it } from 'vitest';
import { ROAD_W } from '../src/config';
import {
  buildingRadius,
  CENTRE_REACH,
  clearOfRoad,
  HOUSE,
  nearestStreet,
  Occupancy,
  TOWN_HOUSE,
  treeChance
} from '../src/scene/placement';
import type { RoadRun } from '../src/scene/roads';

const runs: RoadRun[] = [
  { horizontal: true, fixed: 0, from: -10, to: 10 },
  { horizontal: false, fixed: 20, from: -5, to: 5 }
];

describe('nearestStreet', () => {
  it('measures to the nearest run and faces the house towards it', () => {
    expect(nearestStreet({ x: 0, z: -3 }, runs)).toEqual({ distance: 3, facing: 0 });
    expect(nearestStreet({ x: 0, z: 2 }, runs)).toEqual({ distance: 2, facing: Math.PI });
    expect(nearestStreet({ x: 18, z: 4 }, runs)).toEqual({ distance: 2, facing: Math.PI / 2 });
    expect(nearestStreet({ x: 23, z: 0 }, runs)).toEqual({ distance: 3, facing: -Math.PI / 2 });
  });

  it('measures past the end of a run to its end', () => {
    expect(nearestStreet({ x: -13, z: 4 }, runs).distance).toBeCloseTo(5);
  });

  it('is infinitely far without streets', () => {
    expect(nearestStreet({ x: 0, z: 0 }, []).distance).toBe(Infinity);
  });
});

describe('buildingRadius', () => {
  it('builds town houses near the centre and houses elsewhere', () => {
    const centre = { x: 0, z: 0 };
    expect(buildingRadius({ x: 3, z: 4 }, centre)).toBe(TOWN_HOUSE);
    expect(buildingRadius({ x: CENTRE_REACH, z: 0 }, centre)).toBe(HOUSE);
    expect(buildingRadius({ x: 0, z: 0 }, undefined)).toBe(HOUSE);
  });
});

describe('clearOfRoad', () => {
  it('leaves a pavement between the building and the road', () => {
    expect(clearOfRoad(ROAD_W / 2 + 0.15 + 1, 1)).toBe(true);
    expect(clearOfRoad(ROAD_W / 2 + 0.1 + 1, 1)).toBe(false);
    expect(clearOfRoad(Infinity, 1)).toBe(true);
  });
});

describe('treeChance', () => {
  it('lines the banks out in the fields', () => {
    expect(treeChance(false, false, 2)).toBe(0.3);
    expect(treeChance(true, false, 2)).toBe(0.3);
    expect(treeChance(false, false, 0.5)).toBe(0.07);
  });

  it('is thick in the park and sparse in town', () => {
    expect(treeChance(true, false, 10)).toBe(0.32);
    expect(treeChance(true, true, 2)).toBe(0.32);
    expect(treeChance(false, true, 2)).toBe(0.05);
    expect(treeChance(false, false, 10)).toBe(0.07);
  });
});

describe('Occupancy', () => {
  it('refuses circles that overlap what is taken', () => {
    const ground = new Occupancy();
    expect(ground.free({ x: 0, z: 0 }, 1)).toBe(true);
    ground.take({ x: 0, z: 0 }, 1);
    expect(ground.free({ x: 1.5, z: 0 }, 1)).toBe(false);
    expect(ground.free({ x: 2, z: 0 }, 1)).toBe(true);
  });
});
