import { describe, expect, it } from 'vitest';
import {
  chooseTile,
  cutSpurs,
  type GridFrame,
  joinRoads,
  layRoads,
  OPEN,
  type Openings,
  openSquares,
  pruneRoads,
  RoadGrid,
  roadPieces,
  roadRuns,
  type RouteRules,
  tileTurn
} from '../src/scene/roads';

/** The rows of a text picture, without indentation or blank lines. */
function rows(art: string): string[] {
  return art
    .split(/\r?\n/)
    .map(row => row.trim())
    .filter(row => row.length > 0);
}

/** A grid from a text picture: '#' is road, i runs along a row (east), j down the rows (south). */
function draw(art: string): RoadGrid {
  const grid = new RoadGrid();
  rows(art).forEach((row, j) => [...row].forEach((c, i) => c === '#' && grid.add(i, j)));
  return grid;
}

/** The grid back as a text picture the size of `art`, to compare with it. */
function picture(grid: RoadGrid, art: string): string[] {
  const shape = rows(art);
  return shape.map((row, j) => [...row].map((_, i) => (grid.has(i, j) ? '#' : '.')).join(''));
}

const frame = (nx: number, nz: number): GridFrame => ({ x0: 0, z0: 0, tile: 1, nx, nz, every: { x: 4, z: 3 } });

describe('layRoads', () => {
  it('lays a block grid wherever the ground is usable', () => {
    const grid = layRoads(frame(9, 7), (i, j) => !(i > 4 && j > 3));
    const expected = `
      #########
      #...#...#
      #...#...#
      #########
      #...#....
      #...#....
      #####....
    `;
    expect(picture(grid, expected)).toEqual(rows(expected));
  });
});

describe('pruneRoads', () => {
  it('drops lone cells, stubs off junctions and pieces under four cells', () => {
    const grid = draw(`
      #######..#
      ...#......
      ...#...##.
      ..###.....
      ...#......
      ...#......
    `);
    pruneRoads(grid);
    const expected = `
      #######...
      ...#......
      ...#......
      ...#......
      ...#......
      ...#......
    `;
    expect(picture(grid, expected)).toEqual(rows(expected));
  });

  it('keeps a closed block', () => {
    const block = `
      #####
      #...#
      #####
    `;
    const grid = draw(block);
    pruneRoads(grid);
    expect(picture(grid, block)).toEqual(rows(block));
  });
});

describe('roadRuns', () => {
  it('finds the straight runs along the grid lines, in scene units', () => {
    const grid = draw(`
      #####
      #...#
      #...#
      #####
    `);
    const runs = roadRuns(grid, { x0: 10, z0: 20, tile: 2, nx: 5, nz: 4, every: { x: 4, z: 3 } });
    expect(runs).toEqual([
      { horizontal: true, fixed: 20, from: 9, to: 19 },
      { horizontal: true, fixed: 26, from: 9, to: 19 },
      { horizontal: false, fixed: 10, from: 19, to: 27 },
      { horizontal: false, fixed: 18, from: 19, to: 27 }
    ]);
  });
});

describe('RoadGrid', () => {
  it('knows which sides continue and whether one leads to a junction', () => {
    const grid = draw(`
      .#.
      ###
      .#.
      .#.
    `);
    expect(grid.links(1, 1)).toEqual([1, 1, 1, 1]);
    expect(grid.count(1, 1)).toBe(4);
    expect(grid.links(1, 3)).toEqual([0, 0, 0, 1]);
    expect(grid.besideJunction(1, 2)).toBe(true);
    expect(grid.besideJunction(1, 3)).toBe(false);
  });
});

describe('chooseTile and tileTurn', () => {
  const all: Openings[] = Array.from(
    { length: 16 },
    (_, m) => [m & 1, (m >> 1) & 1, (m >> 2) & 1, (m >> 3) & 1] as const
  );

  it('picks the tile by how many sides continue', () => {
    expect(chooseTile([1, 1, 1, 1], false)).toBe('road-crossroad');
    expect(chooseTile([1, 1, 0, 1], false)).toBe('road-intersection');
    expect(chooseTile([0, 0, 1, 0], false)).toBe('road-end-round');
    expect(chooseTile([0, 1, 0, 1], false)).toBe('road-straight');
    expect(chooseTile([1, 1, 0, 0], false)).toBe('road-bend');
  });

  it('turns a straight piece near the centre into a crossing, nothing else', () => {
    expect(chooseTile([1, 0, 1, 0], true)).toBe('road-crossing');
    expect(chooseTile([1, 1, 0, 0], true)).toBe('road-bend');
  });

  it('turns every tile so its openings meet the neighbouring roads', () => {
    for (const want of all.filter(w => w.some(Boolean))) {
      const tile = chooseTile(want, false);
      const r = Math.round(tileTurn(OPEN[tile], want) / (Math.PI / 2));
      expect(
        want.map((_, s) => OPEN[tile][(s + r) % 4]),
        `${want.join('')} as ${tile}`
      ).toEqual([...want]);
    }
  });
});

describe('joinRoads', () => {
  const rulesFor = (art: string): RouteRules => {
    const map = rows(art);
    return {
      ground: (i, j) => {
        const c = map[j]?.[i] ?? 'x';
        return c === '~' ? 'water' : c === 'x' ? 'blocked' : 'land';
      },
      turn: 2,
      bridge: 2,
      longestBridge: 3
    };
  };

  it('joins two districts across a river with one straight bridge', () => {
    const land = `
      ..~~..
      ..~~..
      ..~~..
    `;
    const grid = draw(`
      #.....
      #.....
      #....#
    `);
    grid.add(5, 1);
    grid.add(5, 0);
    const bridges = joinRoads(grid, frame(6, 3), rulesFor(land));
    expect(roadPieces(grid)).toHaveLength(1);
    expect(bridges).toHaveLength(2);
    expect(new Set(bridges.map(([, j]) => j)).size).toBe(1);
  });

  it('leaves a district that only a long bridge would reach', () => {
    const land = `
      .~~~~.
    `;
    const grid = draw(`
      #....#
    `);
    expect(joinRoads(grid, frame(6, 1), rulesFor(land))).toHaveLength(0);
    expect(roadPieces(grid)).toHaveLength(2);
  });

  it('goes round what is blocked', () => {
    const land = `
      ..x..
      ..x..
      .....
    `;
    const grid = draw(`
      #...#
    `);
    joinRoads(grid, frame(5, 3), rulesFor(land));
    expect(roadPieces(grid)).toHaveLength(1);
    expect(grid.has(2, 0)).toBe(false);
    expect(grid.has(2, 2)).toBe(true);
  });
});

describe('openSquares', () => {
  it('opens a square of road into an L', () => {
    const grid = draw(`
      ###.
      ##..
    `);
    openSquares(grid);
    expect(picture(grid, `....\n....`)).toEqual(['###.', '#...']);
  });
});

describe('cutSpurs', () => {
  it('cuts a short dead end off a junction and keeps a long one', () => {
    const grid = draw(`
      #####
      ..#..
      .....
      ..#..
    `);
    grid.add(2, 2);
    grid.add(2, 4);
    grid.add(2, 5);
    grid.add(2, 6);
    grid.add(2, 7);
    cutSpurs(grid, 2);
    expect(grid.has(0, 0)).toBe(false);
    expect(grid.has(4, 0)).toBe(false);
    expect(grid.has(2, 7)).toBe(true);
  });

  it('keeps the roads out of town', () => {
    const grid = draw(`
      ###
      .#.
    `);
    cutSpurs(grid, 2, new Set(['0,0']));
    expect(grid.has(0, 0)).toBe(true);
    expect(grid.has(2, 0)).toBe(false);
  });
});
