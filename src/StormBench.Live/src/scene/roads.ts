import type { RoadTile } from '../assets/kit';

export type Cell = readonly [i: number, j: number];

export type Openings = readonly [east: number, south: number, west: number, north: number];

export interface RoadRun {
  readonly horizontal: boolean;
  readonly fixed: number;
  readonly from: number;
  readonly to: number;
}

export interface GridFrame {
  readonly x0: number;
  readonly z0: number;
  readonly tile: number;
  readonly nx: number;
  readonly nz: number;
  readonly every: { readonly x: number; readonly z: number };
}

export const SIDES: readonly Cell[] = [
  [1, 0],
  [0, 1],
  [-1, 0],
  [0, -1]
];

export const OPEN: Readonly<Record<RoadTile, Openings>> = {
  'road-straight': [1, 0, 1, 0],
  'road-crossing': [1, 0, 1, 0],
  'road-crossroad': [1, 1, 1, 1],
  'road-intersection': [1, 1, 1, 0],
  'road-bend': [0, 1, 1, 0],
  'road-end-round': [1, 0, 0, 0]
};

const STRIDE = 100000;

/** The road cells of a tile grid, with the questions autotiling asks about them. */
export class RoadGrid {
  private readonly cells = new Set<number>();

  has(i: number, j: number): boolean {
    return this.cells.has(i * STRIDE + j);
  }

  add(i: number, j: number): void {
    this.cells.add(i * STRIDE + j);
  }

  delete(i: number, j: number): void {
    this.cells.delete(i * STRIDE + j);
  }

  get size(): number {
    return this.cells.size;
  }

  /** The cells in the order they were laid. */
  list(): Cell[] {
    return [...this.cells].map(k => [Math.floor(k / STRIDE), k % STRIDE] as const);
  }

  /** Which of the four sides (east, south, west, north) continue as road. */
  links(i: number, j: number): Openings {
    const [e, s, w, n] = SIDES.map(([di, dj]) => (this.has(i + di, j + dj) ? 1 : 0));
    return [e!, s!, w!, n!];
  }

  /** How many sides continue as road. */
  count(i: number, j: number): number {
    return this.links(i, j).reduce((a: number, c) => a + c, 0);
  }

  /** Whether a side that continues leads to a junction (a cell with three or more roads). */
  besideJunction(i: number, j: number): boolean {
    const open = this.links(i, j);
    return SIDES.some(([di, dj], s) => open[s] === 1 && this.count(i + di, j + dj) >= 3);
  }
}

/** Roads on every `every.x`-th column and every `every.z`-th row, wherever `usable(i, j)` allows. */
export function layRoads(frame: GridFrame, usable: (i: number, j: number) => boolean): RoadGrid {
  const grid = new RoadGrid();
  for (let i = 0; i < frame.nx; i++) {
    for (let j = 0; j < frame.nz; j++) {
      if ((i % frame.every.x === 0 || j % frame.every.z === 0) && usable(i, j)) grid.add(i, j);
    }
  }
  return grid;
}

/**
 * Tidies the network: drops cells left on their own and one-cell stubs off a junction (three
 * times, as each pass can make new ones), then every piece of fewer than four cells.
 */
export function pruneRoads(grid: RoadGrid): void {
  for (let pass = 0; pass < 3; pass++) {
    for (const [i, j] of grid.list()) {
      const n = grid.count(i, j);
      if (n === 0 || (n === 1 && grid.besideJunction(i, j))) grid.delete(i, j);
    }
  }
  const seen = new Set<string>();
  for (const [i, j] of grid.list()) {
    if (seen.has(`${i},${j}`)) continue;
    const part: Cell[] = [];
    const stack: Cell[] = [[i, j]];
    seen.add(`${i},${j}`);
    while (stack.length) {
      const [a, c] = stack.pop()!;
      part.push([a, c]);
      for (const [di, dj] of SIDES) {
        const key = `${a + di},${c + dj}`;
        if (grid.has(a + di, c + dj) && !seen.has(key)) {
          seen.add(key);
          stack.push([a + di, c + dj]);
        }
      }
    }
    if (part.length < 4) for (const [a, c] of part) grid.delete(a, c);
  }
}

/** The straight runs of road along the grid lines, as spans in scene units, for houses to face. */
export function roadRuns(grid: RoadGrid, frame: GridFrame): RoadRun[] {
  const { x0, z0, tile, nx, nz, every } = frame;
  const runs: RoadRun[] = [];
  for (let j = 0; j < nz; j += every.z) {
    for (let i = 0; i < nx; i++) {
      if (!grid.has(i, j) || !(grid.has(i + 1, j) || grid.has(i - 1, j))) continue;
      const from = i;
      while (grid.has(i + 1, j)) i++;
      runs.push({
        horizontal: true,
        fixed: z0 + j * tile,
        from: x0 + from * tile - tile / 2,
        to: x0 + i * tile + tile / 2
      });
    }
  }
  for (let i = 0; i < nx; i += every.x) {
    for (let j = 0; j < nz; j++) {
      if (!grid.has(i, j) || !(grid.has(i, j + 1) || grid.has(i, j - 1))) continue;
      const from = j;
      while (grid.has(i, j + 1)) j++;
      runs.push({
        horizontal: false,
        fixed: x0 + i * tile,
        from: z0 + from * tile - tile / 2,
        to: z0 + j * tile + tile / 2
      });
    }
  }
  return runs;
}

/**
 * The tile for a cell whose sides `want` continue as road; a straight piece near the town centre
 * right next to a junction becomes a zebra crossing.
 */
export function chooseTile(want: Openings, crossing: boolean): RoadTile {
  const n = want.reduce((a: number, c) => a + c, 0);
  if (n === 4) return 'road-crossroad';
  if (n === 3) return 'road-intersection';
  if (n === 1) return 'road-end-round';
  if (want[0] !== want[2]) return 'road-bend';
  return crossing ? 'road-crossing' : 'road-straight';
}

/** The quarter turn (radians about y) that lines the tile's openings up with `want`; 0 if none does. */
export function tileTurn(open: Openings, want: Openings): number {
  for (let r = 0; r < 4; r++) {
    if (want.every((w, s) => w === open[(s + r) % 4])) return (r * Math.PI) / 2;
  }
  return 0;
}

/** What a cell costs a road to cross: land, water that only a straight bridge may cross, or blocked. */
export type Ground = 'land' | 'water' | 'blocked';

/**
 * How a road is routed: what each cell is, what a turn costs, what a bridge cell costs, how long a
 * bridge may be, and what each road cell beside a step costs, so a new road does not run along
 * an old one.
 */
export interface RouteRules {
  readonly ground: (i: number, j: number) => Ground;
  readonly turn: number;
  readonly bridge: number;
  readonly longestBridge: number;
  readonly beside?: number;
  readonly near?: number;
  readonly extra?: (i: number, j: number) => number;
  readonly bank?: (i: number, j: number) => string;
}

const NEAR: readonly Cell[] = [
  [2, 0],
  [0, 2],
  [-2, 0],
  [0, -2],
  [1, 1],
  [1, -1],
  [-1, 1],
  [-1, -1]
];

/** The connected pieces of a road grid, largest first. */
export function roadPieces(grid: RoadGrid): Cell[][] {
  const seen = new Set<string>();
  const pieces: Cell[][] = [];
  for (const [i, j] of grid.list()) {
    if (seen.has(`${i},${j}`)) continue;
    const piece: Cell[] = [];
    const stack: Cell[] = [[i, j]];
    seen.add(`${i},${j}`);
    while (stack.length) {
      const [a, c] = stack.pop()!;
      piece.push([a, c]);
      for (const [di, dj] of SIDES) {
        const key = `${a + di},${c + dj}`;
        if (grid.has(a + di, c + dj) && !seen.has(key)) {
          seen.add(key);
          stack.push([a + di, c + dj]);
        }
      }
    }
    pieces.push(piece);
  }
  return pieces.sort((a, b) => b.length - a.length);
}

/** A binary heap of states by cost. */
class Queue {
  private readonly items: { cost: number; state: number }[] = [];

  get size(): number {
    return this.items.length;
  }

  push(cost: number, state: number): void {
    const items = this.items;
    items.push({ cost, state });
    let k = items.length - 1;
    while (k > 0) {
      const parent = (k - 1) >> 1;
      if (items[parent]!.cost <= items[k]!.cost) break;
      [items[parent], items[k]] = [items[k]!, items[parent]!];
      k = parent;
    }
  }

  pop(): { cost: number; state: number } {
    const items = this.items;
    const top = items[0]!;
    const last = items.pop()!;
    if (items.length) {
      items[0] = last;
      let k = 0;
      for (;;) {
        const l = 2 * k + 1;
        const r = l + 1;
        let m = k;
        if (l < items.length && items[l]!.cost < items[m]!.cost) m = l;
        if (r < items.length && items[r]!.cost < items[m]!.cost) m = r;
        if (m === k) break;
        [items[m], items[k]] = [items[k]!, items[m]!];
        k = m;
      }
    }
    return top;
  }
}

/**
 * The cheapest road from any cell of `from` to any cell `isGoal` accepts, over the grid of `frame`:
 * a step costs 1, a turn `turn` more, a step over water `bridge` more, and each road cell beside
 * the step `beside` more (one cell further, `near`). Over water the road goes straight, no further
 * than `longestBridge` cells, comes out on another bank than it went in on (`bank` names a cell's
 * side of its water), and never starts or ends there. Returns the cells it lays, goal included,
 * or null if there is no way.
 */
export function routeRoad(
  frame: GridFrame,
  from: readonly Cell[],
  isGoal: (i: number, j: number) => boolean,
  rules: RouteRules,
  roads: RoadGrid | null = null
): Cell[] | null {
  const { nx, nz } = frame;
  const states = nx * nz * 4;
  const cost = new Float64Array(states).fill(Infinity);
  const back = new Int32Array(states).fill(-1);
  const span = new Int16Array(states);
  const entry = new Int32Array(states).fill(-1);
  const queue = new Queue();
  const id = (i: number, j: number, d: number) => (j * nx + i) * 4 + d;
  for (const [i, j] of from) {
    for (let d = 0; d < 4; d++) {
      cost[id(i, j, d)] = 0;
      queue.push(0, id(i, j, d));
    }
  }
  const starts = new Set(from.map(([i, j]) => j * nx + i));
  while (queue.size) {
    const { cost: c, state } = queue.pop();
    if (c > cost[state]!) continue;
    const d = state % 4;
    const cell = (state - d) / 4;
    const i = cell % nx;
    const j = (cell - i) / nx;
    const here = rules.ground(i, j);
    if (here === 'land' && !starts.has(cell) && isGoal(i, j)) {
      const path: Cell[] = [];
      for (let s = state; s >= 0; s = back[s]!) {
        const sd = s % 4;
        const sc = (s - sd) / 4;
        const si = sc % nx;
        const cellOf: Cell = [si, (sc - si) / nx];
        path.push(cellOf);
      }
      return path.reverse();
    }
    for (let nd = 0; nd < 4; nd++) {
      if (here === 'water' && nd !== d) continue;
      const [di, dj] = SIDES[nd]!;
      const ni = i + di;
      const nj = j + dj;
      if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
      const next = rules.ground(ni, nj);
      if (next === 'blocked') continue;
      const run = next === 'water' ? (here === 'water' ? span[state]! + 1 : 1) : 0;
      if (run > rules.longestBridge) continue;
      const enteredAt = next === 'water' ? (here === 'water' ? entry[state]! : cell) : -1;
      if (here === 'water' && next === 'land' && rules.bank) {
        const from = entry[state]!;
        const fi = from % nx;
        if (rules.bank(fi, (from - fi) / nx) === rules.bank(ni, nj)) continue;
      }
      let crowd = 0;
      if (roads && !isGoal(ni, nj)) {
        for (const [ai, aj] of SIDES) {
          const bi = ni + ai;
          const bj = nj + aj;
          if ((bi !== i || bj !== j) && roads.has(bi, bj)) crowd += rules.beside ?? 0;
        }
        for (const [ai, aj] of NEAR) {
          const bi = ni + ai;
          const bj = nj + aj;
          if ((bi !== i || bj !== j) && roads.has(bi, bj)) crowd += rules.near ?? 0;
        }
      }
      const step =
        1 +
        (nd !== d && back[state]! >= 0 ? rules.turn : 0) +
        (next === 'water' ? rules.bridge : 0) +
        crowd +
        (rules.extra?.(ni, nj) ?? 0);
      const ns = id(ni, nj, nd);
      if (c + step < cost[ns]!) {
        cost[ns] = c + step;
        back[ns] = state;
        span[ns] = run;
        entry[ns] = enteredAt;
        queue.push(c + step, ns);
      }
    }
  }
  return null;
}

/**
 * Joins the pieces of a road grid into one network: each step the piece that holds the largest
 * one is joined to the nearest other piece by the cheapest road, bridges included, until all are
 * one or the rest cannot be reached. Returns the cells over water, which carry bridges.
 */
export function joinRoads(grid: RoadGrid, frame: GridFrame, rules: RouteRules): Cell[] {
  const bridges: Cell[] = [];
  for (let guard = 0; guard < 64; guard++) {
    const pieces = roadPieces(grid);
    if (pieces.length < 2) break;
    const main = pieces[0]!;
    const inMain = new Set(main.map(([i, j]) => `${i},${j}`));
    const path = routeRoad(frame, main, (i, j) => grid.has(i, j) && !inMain.has(`${i},${j}`), rules, grid);
    if (!path) break;
    for (const [i, j] of path) {
      grid.add(i, j);
      if (rules.ground(i, j) === 'water') bridges.push([i, j]);
    }
  }
  return bridges;
}

/** How awkward a cell draws: nothing for a straight road, one for a bend or a dead end, two for a junction. */
function awkwardness(grid: RoadGrid, i: number, j: number): number {
  const [e, s, w, n] = grid.links(i, j);
  const count = e! + s! + w! + n!;
  if (count >= 3) return 2;
  if (count === 2) return e === w ? 0 : 1;
  return 1;
}

/**
 * Opens every 2 x 2 square of road, which would draw as a little ring with a hole. One of its
 * cells with no road going on outside the square is dropped: the one that leaves the fewest
 * bends, junctions and dead ends.
 */
export function openSquares(grid: RoadGrid): void {
  for (let changed = true; changed;) {
    changed = false;
    for (const [i, j] of grid.list()) {
      const square: Cell[] = [
        [i, j],
        [i + 1, j],
        [i, j + 1],
        [i + 1, j + 1]
      ];
      if (!square.every(([a, c]) => grid.has(a, c))) continue;
      const inSquare = (a: number, c: number) => a >= i && a <= i + 1 && c >= j && c <= j + 1;
      const outside = ([a, c]: Cell) =>
        SIDES.filter(([di, dj]) => !inSquare(a + di, c + dj) && grid.has(a + di, c + dj)).length;
      const cost = ([a, c]: Cell) => {
        grid.delete(a, c);
        const total = square
          .filter(([x, y]) => x !== a || y !== c)
          .reduce((sum, [x, y]) => sum + awkwardness(grid, x, y), 0);
        grid.add(a, c);
        return total;
      };
      const free = square.filter(cell => outside(cell) === 0);
      if (!free.length) continue;
      const loose = free.sort((x, y) => cost(x) - cost(y))[0]!;
      grid.delete(loose[0], loose[1]);
      changed = true;
    }
  }
}

/**
 * Cuts the short dead ends off the network, in one pass: a spur of at most `longest` cells from a
 * junction to nothing. Cells in `keep` (the roads out of town) stay, and so does a street that is a dead end
 * at both ends.
 */
export function cutSpurs(grid: RoadGrid, longest: number, keep: ReadonlySet<string> = new Set()): void {
  const doomed: Cell[] = [];
  for (const [i, j] of grid.list()) {
    if (!grid.has(i, j) || grid.count(i, j) !== 1 || keep.has(`${i},${j}`)) continue;
    const spur: Cell[] = [[i, j]];
    let prev: Cell | null = null;
    let at: Cell = [i, j];
    let junction = false;
    while (spur.length <= longest) {
      const next = SIDES.map(([di, dj]): Cell => [at[0] + di, at[1] + dj]).find(
        ([a, c]) => grid.has(a, c) && !(prev && prev[0] === a && prev[1] === c)
      );
      if (!next) break;
      if (grid.count(next[0], next[1]) >= 3) {
        junction = true;
        break;
      }
      if (keep.has(`${next[0]},${next[1]}`)) break;
      prev = at;
      at = next;
      spur.push(next);
    }
    if (!junction || spur.length > longest) continue;
    doomed.push(...spur);
  }
  for (const [a, c] of doomed) grid.delete(a, c);
}
