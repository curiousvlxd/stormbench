export type MapPoint = readonly [x: number, y: number];

export interface NodeDescription {
  readonly id: string;
  readonly kind: string;
  readonly x: number;
  readonly y: number;
}

export interface LinkDescription {
  readonly id: string;
  readonly kind: string;
  readonly from: string;
  readonly to: string;
}

export interface StationDescription {
  readonly valve: string;
  readonly tank: string;
  readonly baseSetting: number;
  readonly lowSetting: number;
}

export interface CatchmentDescription {
  readonly id: string;
  readonly rainGauge: string;
  readonly outlet: string;
  readonly areaHa: number;
  readonly polygon: readonly MapPoint[] | null;
}

export interface GaugeDescription {
  readonly id: string;
  readonly x: number;
  readonly y: number;
}

export interface WaterDescription {
  readonly name: string;
  readonly line: readonly MapPoint[] | null;
}

export interface AreaDescription {
  readonly name: string;
  readonly polygon: readonly MapPoint[];
}

export interface PlaceDescription {
  readonly name: string;
  readonly x: number;
  readonly y: number;
}

export interface MapDescription {
  readonly catchments?: readonly CatchmentDescription[] | null;
  readonly gauges?: readonly GaugeDescription[] | null;
  readonly waters?: readonly WaterDescription[] | null;
  readonly areas?: readonly AreaDescription[] | null;
  readonly places?: readonly PlaceDescription[] | null;
  readonly tints?: Readonly<Record<string, string>> | null;
}

export interface NetworkDescription {
  readonly name: string;
  readonly controlStepS: number;
  readonly tankMaxDepthM: number;
  readonly wwtp: string;
  readonly tanks: readonly string[];
  readonly overflowPoints: readonly string[];
  readonly stations: readonly StationDescription[];
  readonly nodes: readonly NodeDescription[];
  readonly links: readonly LinkDescription[];
  readonly labelOffsets?: Readonly<Record<string, readonly number[]>> | null;
  readonly map?: MapDescription | null;
}

export type AlarmLevel = 'high' | 'medium' | 'low';

export interface LiveTank {
  readonly id: string;
  readonly fill: number | null;
  readonly trueFill: number | null;
  readonly overflowing: boolean;
  readonly alarm: AlarmLevel | null;
}

export interface LiveGate {
  readonly id: string;
  readonly setpoint: number | null;
  readonly opening: number | null;
  readonly alarm: AlarmLevel | null;
}

export interface LiveWeir {
  readonly id: string;
  readonly overflowing: boolean;
  readonly alarm: AlarmLevel | null;
}

export type LinkState = 'unused' | 'up' | 'cut' | 'lost';

export type PlcState = 'online' | 'offline' | 'waiting';

export interface LiveFrame {
  readonly running: boolean;
  readonly rain: number | null;
  readonly tanks: readonly LiveTank[];
  readonly gates: readonly LiveGate[];
  readonly weirs: readonly LiveWeir[];
  readonly volumes: Readonly<Record<string, number>> | null;
  readonly wwtp: number | null;
  readonly link: LinkState;
  readonly plc: PlcState;
  readonly plcLocal: boolean;
  readonly hour: number | null;
}

export interface DotNetObject {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}
