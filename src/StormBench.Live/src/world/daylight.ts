/**
 * Day and night over the town, from the simulated clock. The benchmark's storms fall in late May
 * in western Germany: the sun rises a little after five and sets about half past nine, with half
 * an hour or so of twilight either side.
 */
const DAWN = [4.6, 5.9] as const;
const DUSK = [20.9, 22.2] as const;

const smooth = (a: number, b: number, x: number): number => {
  const t = Math.min(1, Math.max(0, (x - a) / (b - a)));
  return t * t * (3 - 2 * t);
};

/** How much daylight there is at `hour` (0..24): 1 by day, 0 at night, easing through dawn and dusk. */
export function daylight(hour: number): number {
  const h = ((hour % 24) + 24) % 24;
  return smooth(DAWN[0], DAWN[1], h) * (1 - smooth(DUSK[0], DUSK[1], h));
}

/** How much of the warm light of sunrise or sunset there is at `hour`, 0..1, highest in the middle of each. */
export function twilight(hour: number): number {
  const day = daylight(hour);
  return Math.sin(Math.PI * day) ** 2;
}

/** Whether the street lamps are on at a daylight of `day`: they come on in the dusk, before it is dark. */
export function lampsOn(day: number): number {
  return 1 - smooth(0.35, 0.75, day);
}
