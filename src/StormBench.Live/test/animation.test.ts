import { Vector3 } from 'three';
import { describe, expect, it } from 'vitest';
import { easeInOut } from '../src/camera/CameraDirector';
import { plumeAmount, streamShape } from '../src/scene/Outfalls';
import { rainTarget, splashBrightness } from '../src/scene/Weather';
import { freeSpot, Obstacles } from '../src/world/Obstacles';
import { waterPath } from '../src/world/water';

describe('easeInOut', () => {
  it('starts and ends still and passes the middle at half way', () => {
    expect(easeInOut(0)).toBe(0);
    expect(easeInOut(0.5)).toBe(0.5);
    expect(easeInOut(1)).toBe(1);
    expect(easeInOut(0.1)).toBeLessThan(0.1);
    expect(easeInOut(0.9)).toBeGreaterThan(0.9);
  });
});

describe('plumeAmount', () => {
  it('grows with the volume spilled and stays below one', () => {
    expect(plumeAmount(0)).toBe(0);
    expect(plumeAmount(0.4)).toBe(0);
    expect(plumeAmount(1)).toBeCloseTo(0.121, 3);
    expect(plumeAmount(900)).toBeCloseTo(0.12 + 0.88 * (1 - Math.exp(-1)));
    expect(plumeAmount(1e7)).toBeCloseTo(1);
  });
});

describe('rainTarget', () => {
  it('shows no rain under a trace and full rain from 20 mm/h', () => {
    expect(rainTarget(0)).toBe(0);
    expect(rainTarget(0.05)).toBe(0);
    expect(rainTarget(11)).toBeCloseTo(0.58);
    expect(rainTarget(40)).toBe(1);
  });
});

describe('splashBrightness', () => {
  it('flashes up quickly and fades out', () => {
    expect(splashBrightness(0)).toBe(0);
    expect(splashBrightness(0.15)).toBe(1);
    expect(splashBrightness(0.325)).toBeCloseTo(0.5);
    expect(splashBrightness(0.9)).toBe(0);
  });
});

describe('Obstacles', () => {
  it('measures to the nearest circle, segment or bank', () => {
    const river = waterPath('River', [new Vector3(-50, 0, 30), new Vector3(50, 0, 30)], 4);
    const obstacles = new Obstacles(
      [[new Vector3(0, 0, 0), 2]],
      [[new Vector3(10, 0, -10), new Vector3(10, 0, 10), 0.5]],
      [river]
    );
    expect(obstacles.clearance(new Vector3(5, 0, 0))).toBeCloseTo(3);
    expect(obstacles.clearance(new Vector3(9, 0, 5))).toBeCloseTo(0.5);
    expect(obstacles.clearance(new Vector3(-20, 0, 25))).toBeCloseTo(5 - 2 - 1.2);
  });
});

describe('freeSpot', () => {
  it('keeps the spot when it is free and looks around it when not', () => {
    const pos = new Map([
      ['A', new Vector3(0, 0, 0)],
      ['B', new Vector3(10, 0, 0)]
    ]);
    const links = [{ id: 'L', kind: 'conduit', from: 'A', to: 'B' }];
    const far = new Vector3(0, 0, 30);
    expect(freeSpot(far, 3, pos, links)).toBe(far);
    const spot = freeSpot(new Vector3(5, 0, 0), 3, pos, links);
    expect(spot.distanceTo(pos.get('A')!)).toBeGreaterThan(3 + 1.9);
    expect(spot.distanceTo(pos.get('B')!)).toBeGreaterThan(3 + 1.9);
    expect(Math.abs(spot.z)).toBeGreaterThan(3 * 0.6);
  });
});

describe('streamShape', () => {
  it('throws farther and runs wider the harder the outfall runs', () => {
    const low = streamShape(0.1);
    const high = streamShape(1);
    expect(high.throw).toBeGreaterThan(low.throw);
    expect(high.width).toBeGreaterThan(low.width);
    expect(streamShape(5)).toEqual(streamShape(1));
    expect(streamShape(-1)).toEqual(streamShape(0));
  });
});
