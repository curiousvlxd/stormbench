import {
  BufferGeometry,
  Color,
  type HSL,
  Line,
  LineBasicMaterial,
  Mesh,
  Path,
  type Scene,
  Shape,
  ShapeGeometry,
  Vector2,
  Vector3
} from 'three';
import type { MultiPolygon, Polygon, Ring } from 'polygon-clipping';
import { flat } from '../assets/materials';
import { ringOf, withoutWaters } from '../world/land';
import type { World } from '../world/World';

const GRASS = 0x8fc760;
const PARK = 0x6fbf4a;
const FIELD = 450;

function points(ring: Ring): Vector2[] {
  return ring.slice(0, -1).map(([x, z]) => new Vector2(x, -z));
}

/** The pieces of ground of a multipolygon as one flat mesh at height y. */
function land(parts: MultiPolygon, y: number, color: Color | number): Mesh {
  const shapes = parts.map(([outer, ...holes]) => {
    const shape = new Shape(points(outer!));
    shape.holes.push(...holes.map(h => new Path(points(h))));
    return shape;
  });
  const geometry = new ShapeGeometry(shapes, 24);
  geometry.rotateX(-Math.PI / 2);
  const mesh = new Mesh(geometry, flat(color, { roughness: 1 }));
  mesh.position.y = y;
  mesh.receiveShadow = true;
  return mesh;
}

/** A subcatchment's tint, a little richer and darker so it reads on the grass. */
function catchmentTint(tint: string): Color {
  const color = new Color(tint);
  const hsl: HSL = { h: 0, s: 0, l: 0 };
  color.getHSL(hsl);
  return color.setHSL(hsl.h, Math.min(1, hsl.s * 1.1 + 0.15), Math.max(0.45, hsl.l - 0.14));
}

/** The outlines of a multipolygon's rings, just above the ground. */
function outlines(parts: MultiPolygon, y: number, color: Color): Line[] {
  const material = new LineBasicMaterial({ color });
  return parts.flat().map(ring => {
    const line = new Line(new BufferGeometry().setFromPoints(ring.map(([x, z]) => new Vector3(x, y, z))), material);
    return line;
  });
}

/**
 * The ground: one solid field of grass with the subcatchments as tinted patches and the parks on
 * it, cut only where the waters run in their channels. The tanks lie under it.
 */
export function buildGround(scene: Scene, world: World): void {
  const field: Polygon = [
    [
      [-FIELD, -FIELD],
      [FIELD, -FIELD],
      [FIELD, FIELD],
      [-FIELD, FIELD],
      [-FIELD, -FIELD]
    ]
  ];
  scene.add(land(withoutWaters(field, world.waters), 0, GRASS));
  for (const c of world.catchments) {
    const tint = catchmentTint(c.tint);
    const parts = withoutWaters([ringOf(c.poly)], world.waters);
    scene.add(land(parts, 0.012, new Color(GRASS).lerp(tint, 0.4)));
    for (const line of outlines(parts, 0.03, tint.clone().multiplyScalar(0.7))) scene.add(line);
  }
  for (const a of world.areas) scene.add(land(withoutWaters([ringOf(a.poly)], world.waters), 0.014, PARK));
}
