import { Vector3 } from 'three';
import type { LiveScene } from '../LiveScene';

type Triple = readonly [number, number, number];

export interface FilmApi {
  manual(on: boolean): void;
  advance(dt: number): void;
  opening(): void;
  topDown(): void;
  reveal(): void;
  clean(on: boolean): void;
  dry(on: boolean): void;
  project(): { id: string; x: number; y: number }[];
  look(target: Triple, position: Triple): void;
  readonly debug: LiveScene;
  nodes(): { id: string; at: number[]; water: number }[];
  places(): { name: string; at: number[] }[];
}

declare global {
  interface Window {
    __liveFilm?: FilmApi;
  }
}

/**
 * Film mode (?film): the hooks the recording scripts drive through `window.__liveFilm`: a manual
 * clock, the opening and top-down shots, the reveal, a clean frame without labels, a dry town,
 * where the rain gauges are on screen, camera moves, and the scene itself for inspection.
 */
export function installFilmMode(live: LiveScene): FilmApi {
  const api: FilmApi = {
    manual: on => live.setManual(!!on),
    advance: dt => live.tick(dt),
    opening: () => live.openingShot(),
    topDown: () => live.topDown(),
    reveal: () => live.reveal(),
    clean: on => {
      document.body.classList.toggle('film-clean', !!on);
    },
    dry: on => live.dry(!!on),
    project: () => live.projectGauges(),
    look: (target, position) => live.flyTo(new Vector3(...target), new Vector3(...position)),
    debug: live,
    nodes: () => [...live.pos].map(([id, q]) => ({ id, at: q.toArray(), water: live.nearestWater(q).distance })),
    places: () => live.placeList()
  };
  window.__liveFilm = api;
  return api;
}
