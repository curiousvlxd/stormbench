import { loadAssets } from './assets/load';
import { LiveScene } from './LiveScene';
import type { DotNetObject, LiveFrame, NetworkDescription } from './types';

export interface LiveHandle {
  update(frame: LiveFrame): void;
  focus(id: string | null): void;
  overview(): void;
  dispose(): void;
}

/** Builds the Live 3D view in `host` for the network once the assets are in; the page drives it through the handle. */
export async function create(
  host: HTMLElement,
  network: NetworkDescription,
  dotnet: DotNetObject
): Promise<LiveHandle> {
  const assets = await loadAssets();
  const film = new URLSearchParams(location.search).has('film');
  const scene = new LiveScene(host, network, dotnet, assets, film);
  return {
    update: frame => scene.update(frame),
    focus: id => scene.focus(id),
    overview: () => scene.overview(),
    dispose: () => scene.dispose()
  };
}
