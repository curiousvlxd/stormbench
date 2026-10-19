import { describe, expect, it } from 'vitest';
import type { LiveGate, LiveTank } from '../src/types';
import {
  gateLabel,
  labelRank,
  placeKind,
  plainName,
  plcLabel,
  roomLabel,
  tankLabel,
  weirLabel
} from '../src/ui/labelText';

const tank = (patch: Partial<LiveTank>): LiveTank => ({
  id: 'T1',
  fill: 0.5,
  trueFill: null,
  overflowing: false,
  alarm: null,
  ...patch
});

const gate = (patch: Partial<LiveGate>): LiveGate => ({
  id: 'V1',
  setpoint: 0.4,
  opening: 0.35,
  alarm: null,
  ...patch
});

describe('plainName', () => {
  it('spells out tanks, overflows and gates', () => {
    expect(plainName('T3')).toBe('Tank 3');
    expect(plainName('CSO10')).toBe('Overflow 10');
    expect(plainName('V2')).toBe('Gate 2');
  });

  it('leaves other names as they are', () => {
    expect(plainName('Treatment plant')).toBe('Treatment plant');
    expect(plainName('T3a')).toBe('T3a');
    expect(plainName('RG1 rain gauge')).toBe('RG1 rain gauge');
  });
});

describe('tankLabel', () => {
  it('shows the fill as a percentage', () => {
    expect(tankLabel(tank({ fill: 0.436 }))).toEqual({ text: '44%', state: '' });
  });

  it('shows dashes without a reading', () => {
    expect(tankLabel(tank({ fill: null })).text).toBe('--');
  });

  it('shows the real level and what a frozen sensor says', () => {
    expect(tankLabel(tank({ fill: 0.3, trueFill: 0.72 })).text).toBe('72% (sensor 30%)');
  });

  it('marks alarms over spills', () => {
    expect(tankLabel(tank({ overflowing: true })).state).toBe('spill');
    expect(tankLabel(tank({ overflowing: true, alarm: 'high' })).state).toBe('alarm-high');
  });
});

describe('gateLabel', () => {
  it('shows where the gate is, or the command without feedback', () => {
    expect(gateLabel(gate({}))).toEqual({ text: '35% open', state: '' });
    expect(gateLabel(gate({ opening: null })).text).toBe('40% open');
  });

  it('shows dashes without a command and stuck in alarm', () => {
    expect(gateLabel(gate({ setpoint: null })).text).toBe('--');
    expect(gateLabel(gate({ alarm: 'medium' }))).toEqual({ text: 'stuck', state: 'alarm-medium' });
  });
});

describe('structure labels', () => {
  it('marks a spilling weir', () => {
    expect(weirLabel({ id: 'CSO7', overflowing: true, alarm: null })).toEqual({ text: '', state: 'spill' });
    expect(weirLabel({ id: 'CSO7', overflowing: true, alarm: 'low' }).state).toBe('alarm-low');
  });

  it('flags a cut or lost link on the control room', () => {
    expect(roomLabel('up')).toEqual({ text: '', state: '' });
    expect(roomLabel('unused').text).toBe('');
    expect(roomLabel('cut')).toEqual({ text: 'link cut', state: 'alarm-high' });
    expect(roomLabel('lost').state).toBe('alarm-high');
  });

  it('says when the PLC is offline or on its own', () => {
    expect(plcLabel('online', false)).toEqual({ text: '', state: '' });
    expect(plcLabel('online', true)).toEqual({ text: 'on its own', state: 'alarm-medium' });
    expect(plcLabel('offline', true)).toEqual({ text: 'offline', state: 'alarm-high' });
  });
});

describe('labelRank', () => {
  it('puts alarms first, gates last', () => {
    expect(labelRank('gate', 'alarm-low')).toBe(0);
    expect(labelRank('weir', 'spill')).toBe(1);
    expect(labelRank('tank', '')).toBe(2);
    expect(labelRank('plant', '')).toBe(3);
    expect(labelRank('gate', '')).toBe(4);
  });
});

describe('placeKind', () => {
  it('styles rivers, creeks and areas', () => {
    expect(placeKind('Main River')).toBe('water');
    expect(placeKind('Park Creek')).toBe('creek');
    expect(placeKind('Town Centre')).toBe('area');
  });
});
