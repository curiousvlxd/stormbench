const STEP = 0x6d2b79f5;

/** A small deterministic generator (mulberry32) that starts `skip` draws into its sequence. */
export function random(seed: number, skip = 0): () => number {
  let state = (seed + Math.imul(skip, STEP)) >>> 0;
  return () => {
    state = (state + STEP) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * The town's generator, so it looks the same on every load. The first 227218 draws of the seed
 * once went to procedural textures nothing showed; starting past them keeps the town as it was.
 */
export function townRandom(): () => number {
  return random(20000530, 227218);
}

/** The minimal standard Lehmer generator, for the hand-drawn textures that have always used it. */
export function lehmer(seed: number): () => number {
  let a = seed;
  return () => (a = (a * 16807) % 2147483647) / 2147483647;
}
