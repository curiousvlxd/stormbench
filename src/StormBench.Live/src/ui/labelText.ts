import type { LinkState, LiveGate, LiveTank, LiveWeir, PlcState } from '../types';

export interface LabelValue {
  readonly text: string;
  readonly state: string;
}

const PLAIN: readonly (readonly [RegExp, string])[] = [
  [/^T(\d+)$/, 'Tank'],
  [/^CSO(\d+)$/, 'Overflow'],
  [/^V(\d+)$/, 'Gate']
];

/** The name people read: Tank 3 for T3, Overflow 7 for CSO7, Gate 2 for V2; anything else as it is. */
export function plainName(id: string): string {
  for (const [pattern, word] of PLAIN) {
    const match = pattern.exec(id);
    if (match) return `${word} ${match[1]}`;
  }
  return id;
}

/** A fraction as a whole percentage. */
function percent(fraction: number): string {
  return `${Math.round(fraction * 100)}%`;
}

/** A tank's fill, and what its sensor says while the sensor is frozen. */
export function tankLabel(tank: LiveTank): LabelValue {
  const real = tank.trueFill ?? tank.fill;
  const shown = real == null ? '--' : percent(real);
  const state = tank.alarm ? `alarm-${tank.alarm}` : tank.overflowing ? 'spill' : '';
  const text = tank.trueFill != null ? `${shown} (sensor ${percent(tank.fill ?? 0)})` : shown;
  return { text, state };
}

/** A gate's opening, or stuck while its alarm is up. */
export function gateLabel(gate: LiveGate): LabelValue {
  const text = gate.alarm ? 'stuck' : gate.setpoint == null ? '--' : `${percent(gate.opening ?? gate.setpoint)} open`;
  return { text, state: gate.alarm ? `alarm-${gate.alarm}` : '' };
}

/** An overflow structure shows no value, only whether it is in alarm or spilling. */
export function weirLabel(weir: LiveWeir): LabelValue {
  return { text: '', state: weir.alarm ? `alarm-${weir.alarm}` : weir.overflowing ? 'spill' : '' };
}

/** The control room's label: empty while the link to the PLC holds. */
export function roomLabel(link: LinkState): LabelValue {
  const cut = link === 'cut' || link === 'lost';
  return { text: cut ? 'link cut' : '', state: cut ? 'alarm-high' : '' };
}

/** The PLC's label: offline, on its own (the stations run locally), or empty. */
export function plcLabel(plc: PlcState, local: boolean): LabelValue {
  if (plc === 'offline') return { text: 'offline', state: 'alarm-high' };
  return local ? { text: 'on its own', state: 'alarm-medium' } : { text: '', state: '' };
}

/** Which labels win a crowded spot, lowest first: alarms, spills, tanks, the rest, gates. */
export function labelRank(kind: string, state: string): number {
  if (state.startsWith('alarm')) return 0;
  if (state === 'spill') return 1;
  if (kind === 'tank') return 2;
  if (kind === 'gate') return 4;
  return 3;
}

/** The style of a place name: the river, a creek or an area. */
export function placeKind(name: string): 'water' | 'creek' | 'area' {
  return /river/i.test(name) ? 'water' : /creek/i.test(name) ? 'creek' : 'area';
}
