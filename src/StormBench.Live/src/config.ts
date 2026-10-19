import type { AlarmLevel } from './types';

export const SCALE = 0.01;
export const TANK_R = 1.9;
export const TANK_H = 4.6;
export const TANK_OUTER = TANK_R + 0.32;
export const TANK_SOIL = TANK_OUTER + 1.1;
export const TANK_CLEAR = TANK_SOIL + 0.2;
export const TANK_LENS = TANK_SOIL + 0.25;
export const TANK_REACH = TANK_LENS + 0.12 + 0.8 + 0.5;
export const RIVER_Y = -0.75;
export const RAIN_DROPS = 9000;
export const SPLASHES = 700;
export const TILE = 1.8;
export const ROAD_W = TILE;
export const BLOCK_X = 4 * TILE;
export const BLOCK_Z = 3 * TILE;
export const RIVER_SPOTS = 12;
export const FOG_DENSITY = 0.0016;

export const ASSETS = 'live/assets/';
export const KENNEY = 'live/kenney/';
export const MODELS = 'live/models/';

export const SKY = {
  zenith: 0x4f9fe3,
  horizon: 0xe4f2fb,
  fog: 0xd9ecf8,
  stormFog: 0x8f9eac,
  nightZenith: 0x0c1838,
  nightHorizon: 0x2a3c66,
  nightFog: 0x223150,
  warmHorizon: 0xf4a974,
  warmFog: 0xe9b48f
} as const;

export const WATER_WIDTH: Readonly<Record<string, number>> = { 'Main River': 5.5, 'Park Creek': 2.2 };
export const DEFAULT_WATER_WIDTH = 5;

export const COLOR = {
  gateOk: 0x2f6f9f,
  gateBad: 0xff4d4f,
  linkUp: 0x0a84d6,
  linkIdle: 0x5b6878,
  linkBad: 0xff4d4f,
  select: 0x7fe3ff,
  plcOk: 0x36d399,
  plcLocal: 0xffa23a
} as const;

export const ALARM_COLOR: Readonly<Record<AlarmLevel, number>> = { high: 0xff3b3b, medium: 0xff9b2f, low: 0xffd84a };

export const PLANT_SITE = { x: 5, z: 1 } as const;
export const PLANT_MIDDLE = { x: 4.6, z: -0.6 } as const;
export const PLANT_RADIUS = 9.5;
