# 3D models

The structures of the Live 3D view: underground tanks with their sections, penstocks, overflow chambers, outfalls, bridges, manhole covers, the PLC cabinet, the control room and the treatment plant. Modelled in Blender, shipped to the browser as GLB.

Building or running StormBench does not need Blender: the exported models are committed in `src/StormBench.Scada/wwwroot/live/models/`. Blender is needed only to change a model.

## Files

| Path | What it is |
|---|---|
| `stormbench.blend` | The source. One collection per model, laid out side by side. |
| `export.py` | Blender script: writes each top-level collection as a GLB, moved to the origin. |
| `build.mjs` | The pipeline: Blender export, then glTF Transform, then validation. |
| `validate.mjs` | Checks every shipped GLB with the Khronos glTF Validator. CI runs it. |

## Change a model

1. Open `stormbench.blend` in Blender 5.1 or newer and edit the collection.
2. Export and optimise:

   ```bash
   cd assets/models
   npm ci
   BLENDER=/path/to/blender npm run export
   ```

   `build.mjs` runs Blender in the background, optimises each GLB with [glTF Transform](https://gltf-transform.dev) (meshopt geometry compression; textures stay lossless PNG because they are colour palettes; nothing joined, flattened, simplified or pruned) and validates the result.
3. Commit the `.blend` and the changed files in `wwwroot/live/models/` together.

To check the shipped models only: `npm run validate`.

## Conventions

- **One collection, one model.** The collection name becomes the file name: `TreatmentPlant` is `treatment-plant.glb`. Each collection's `instance_offset` is its origin; the export moves the model there, so the models can stand side by side in the file.
- **Origin and units.** Every model sits on the ground at the origin, +Y up after export. Units are scene units, not metres: the structures are drawn larger than the map scale so they read from the overview. A tank is 1.9 units in inner radius and 4.6 deep, its roof flush with the ground.
- **Roles, not names.** The code finds the parts it moves by a custom property `role`, exported to glTF `extras` and read by three.js as `userData.role`. Names are for people and may change.

  | Role | On | Used for |
  |---|---|---|
  | `surface`, `section` | tank, controlled gate | what stands on the ground, and what lies under it and is shown by the x-ray; the tank's `section` carries `radius`, `height`, `roof`, `floor`, `thickness` and `soil` |
  | `lid` | `TankGroundLine` | the ground over the uncut half of a tank's section: it only hides what lies under it |
  | `wall`, `soil` | tank section | the concrete of the tank and the soil around it, with solid faces where they are cut |
  | `staff`, `sensor` | tank section | the staff gauge on the far wall (`staffBottom`, `staffTop`), the level sensor under the roof |
  | `furniture` | tank surface | hatch, vent and kiosk, hidden while the part of the roof they stand on is cut away; the surface carries `gateBearing`, the free heading for a gate's headstock |
  | `plate`, `spindle` | gates | the penstock plate (`travel`, how far it rises when fully open) and the rising spindle on the headstock |
  | `beacon`, `linkLight`, `statusLed` | tank kiosk, weir kiosk, mast, cabinet | the alarm lamps, the mast's obstruction lamp, the PLC status LED |
  | `label`, `aerial` | most models | where the model's label hangs; where the radio link starts or ends |
  | `mouth` | outfall | where the water leaves the pipe in the headwall's face (`radius` of the pipe) |
  | `jet` | outfall | the stream falling from the mouth into the river, a parabola with its origin at the mouth (`throw`, `fall`); scaled along the throw it is a faster flow. Its UVs run around it and down it, for the flow in the shader |
  | `splash`, `ripple`, `foam` | outfall | where the stream lands: the crown of the splash, a ring that spreads (radius 1) and a patch of foam |
  | `fill` | outfall | the bank filled back behind the headwall, drawn in the bank's own material |
  | `deck`, `pier` | bridge | one span of road deck, and the pier under it |
  | `spin` | `ClarifierBridge_*` | clarifier bridges that turn |
  | `water` | empties | where animated water goes: `shape` is `disc` (with `radius`) or `plane` (with `width`, `depth`) |
  | `bubbles` | empty | aeration bubbles over `width` x `depth`, `count` of them |
  | `pick` | weir chamber | the part that answers a click |
  | `dish` | control room | the dish on the mast |

  The code draws water, spills, lights and labels at these anchors; the models hold only what does not change at run time.
- **Colours.** Our parts take their colour from one small palette texture (`StormBenchPalette`, packed in the `.blend`) through two materials, `Palette` (matte) and `PaletteMetal`. Each face is mapped onto a swatch; the swatches are the design system's colours. The Kenney parts keep their own `colormap` palettes.
- **Low poly.** Flat shading, few segments, bevels as modifiers (applied on export). The look is part of the design; keep it.

## Licences

Our models are CC BY 4.0. The treatment plant hall and the control room building are Kenney's City Kit models (CC0), placed and scaled in our file.
