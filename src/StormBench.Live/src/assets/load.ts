import { RepeatWrapping, type Texture, TextureLoader } from 'three';
import { ASSETS } from '../config';
import { emptyKits, type Kits, loadKits } from './kit';
import { loadStructures, type Structures } from './structures';

export interface Assets {
  readonly water: Texture | null;
  readonly kits: Kits;
  readonly structures: Structures;
}

/**
 * The water normals image, the Kenney low-poly kits (CC0) and our structure models. The kits and
 * the water are optional; the structures are not.
 */
export async function loadAssets(): Promise<Assets> {
  const water = await new TextureLoader()
    .loadAsync(`${ASSETS}waternormals.jpg`)
    .then(t => {
      t.wrapS = t.wrapT = RepeatWrapping;
      return t;
    })
    .catch(() => null);
  const [kits, structures] = await Promise.all([loadKits().catch(() => emptyKits()), loadStructures()]);
  return { water, kits, structures };
}
