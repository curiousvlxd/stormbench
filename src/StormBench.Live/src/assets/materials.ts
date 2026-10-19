import { type ColorRepresentation, MeshStandardMaterial, type MeshStandardMaterialParameters } from 'three';

/** A flat-shaded low-poly material. */
export function flat(color: ColorRepresentation, params: MeshStandardMaterialParameters = {}): MeshStandardMaterial {
  return new MeshStandardMaterial({ color, roughness: 0.85, metalness: 0, flatShading: true, ...params });
}

export interface Palette {
  readonly concreteDark: MeshStandardMaterial;
}

/** The one shared material still made in code, the dark concrete of the open channels; the structures bring theirs. */
export function createPalette(): Palette {
  return { concreteDark: flat(0xa9b0b8) };
}
