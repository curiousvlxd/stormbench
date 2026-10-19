# 0014. A Live 3D view for visitors, next to the operator display

Status: accepted, 2026-10-15; amended 2026-10-19 (see Amendment). The operator display of ADR 0010 and ADR 0013 stays as it is.

## Context

The operator display is built for someone who knows SCADA: ISA-101 symbols, alarm lists, EFD, Base Case. It did not explain itself to a visitor or in a demo: the page was taller than a laptop screen, the key result sat in a small tile, and nothing stated in plain words what was happening or why it mattered. The testbed needs a view that a non-specialist understands in a minute, shows the same live data and can be presented.

## Decision

- **A second view at `/`: the town in a storm, in 3D** (three.js r180, MIT, bundled with the view's TypeScript sources in `src/StormBench.Live` into one module served with the page, no CDN). It is built from `Model/Network` like the operator display: tanks as cutaway vessels with their live level, controlled gates whose plate shows the actual opening and an outline the commanded one, overflow structures, the treatment plant, the field controller (PLC) with its cables to the gates, and the control room (SCADA) with the PLC link as an arc. While a point spills, its channel runs murky and a plume grows in the receiving water with the volume spilled; a spilling tank sheds water over its wall on the river side. Rain falls as hard as the simulator reports.
- **The benchmark's own map, nothing invented.**
  - Node positions and the ten subcatchment polygons come from the `.inp`, the rain gauges RG1 to RG4 from its symbols.
  - Main River, Park Creek, the park and the place names are traced at run time from `astingen.jpg`, the model's georeferenced backdrop (`stormbench_sim/backdrop.py`); each subcatchment takes the backdrop's tint.
  - Houses stand only inside the subcatchments (the paved areas that drain into the sewers), with the town-centre buildings around "Town Centre". Every overflow point drains to the nearest receiving water, river or creek.
  - The only liberty: tanks, drawn far larger than to scale, and chambers next to them move just clear of the water's edge or of each other; their pipes follow.
- **Low-poly, sunny, one family of models.**
  - Houses, town-centre and industrial buildings and trees are Kenney's CC0 kits (instanced, a few draw calls).
  - Tanks, gates, chambers, manholes, the PLC cabinet, the control room and the plant are modelled in Blender (`assets/models/stormbench.blend`), exported to GLB with meshopt compression and coloured from one palette texture in the design system's colours. The code finds the parts it moves by a `role` property (`assets/models/README.md`).
  - The sewers lie underground: on the surface their routes show as trench strips with manhole covers, and the flow as a stream along them. The tanks are sunk 1.2 units into earth banks, the bank and the pit cut open with the wall.
  - A clear sky that turns overcast and grey as it rains; soft sun shadows, ambient occlusion (GTAO), SMAA; light glass panels over the scene.
- **Live versus illustration, stated on the page.** Levels, gate positions, overflows, volumes, rain, the link state and the PLC's fallback are live from the UNS. The houses, the river's course and the flow animation in the pipes are illustration; "How it works" says so.
- **One screen, little text.** Over the scene: a scoreboard with two bars (this run, and the same storm with no control) and the difference; a card for the clicked structure with one sentence; at most two short status lines; and a dock with storm, control, Start and three faults ("Cut link", "Jam gate", "Freeze sensor"). Speed stays at the default; the operator display offers the others. Gate labels appear only when a gate is stuck or selected.
- **Plain words.** T4 is Tank 4, CSO7 is Overflow 7, V2 is Gate 2; Base Case is "No control", EFD is "Smart". The status line (`LiveStory`) states the most important thing first: a cut link and the PLC carrying on alone, a jammed gate or frozen sensor and whether the SCADA has caught it yet, what spills, the rain, and at the end the result.
- **Same modules, same commands.** The page reads `HmiFeed` and sends the same console commands as the operator display; it adds no metric and no endpoint. A frame (`LiveFrame`) goes to the browser only when a module reports a change, at most ten times a second.
- **Always light.** A sunny scene reads best for a visitor; the operator display keeps its light and dark themes.

## Consequences

- A visitor sees in one picture what the testbed does and what smart control is worth; the operator display remains the place for alarm handling and the ISA-101 symbols.
- One more vendored library (about 1 MB of JavaScript with the addons for controls, post-processing, rounded boxes and geometry merging). Its addons import `three` by bare name; the vendored copies rewrite that one import to a relative path and are otherwise unchanged.
- The scene needs WebGL. Headless screenshots run in Edge with the GPU enabled.
- Faults injected on an empty tank show a real limit of the frozen-sensor check: an empty reading that stays put looks like a dry tank. The status line says so, and the "Freeze sensor" button picks a tank that is filling.

## Amendment, 2026-10-19

The first version drew the tanks as silos sunk in earth banks, cut open on one side, with trench strips and a stream for the sewers and open channels to the river. Close up it looked like a stage set: thin walls, see-through surfaces, pipes on the lawn. The town now looks like a town, and what lies underground is shown through an x-ray.

- **Benchmarks first.** `/` lists the benchmark networks; Astlingen opens its sandbox at `/benchmarks/astlingen`, with the operator display at `/benchmarks/astlingen/operator`.
- **Underground, as built.** Tanks, gates and sewers lie under solid ground.
  - On the surface: the tank's roof slab with its hatch, vent and kiosk with the alarm lamp; the gate's headstock with a rising spindle; manhole covers in the streets.
  - At each overflow, an outfall: a concrete headwall in the river bank with wing walls, the pipe's mouth in its face, a chute below, the bank filled back behind it. While it spills, water falls from the mouth into the river, farther and fuller the harder it runs (the stream is a parabola modelled in Blender, so scaling it along the throw gives a faster flow), with a splash, foam and spreading rings where it lands.
  - Rivers run in channels below the ground, their banks shaped by one height function so that two waters meet without a seam. The streets form one network and cross the waters on bridges.
- **X-ray, always on.** The ground over each tank is open to an x-ray. A toggle existed first and was removed: with the lens limited to the tank's own circle and the shaft hiding its neighbours, it only hid what the view is for.
  - The underground is a second scene drawn after the town with its own depth buffer, only inside a lens on the ground over each tank and only where nothing stands in front of it (a stencil mask).
  - The tank is cut in half across the line of sight: soil in layers, the concrete wall, roof and floor with solid section faces, the water at its live level against a staff gauge on the far wall, and the penstock on the wall over the outlet, cut by the same plane. The cut turns with the camera.
  - The removed half stays as glass (faint where it faces the camera, stronger at its outline, with a concrete rim along the top). The tank keeps its whole shape, and everything on its roof (hatch, vent, kiosk, the gate's headstock) stays in place from every side; nothing appears or disappears as the camera turns.
- **Day and night from the simulated clock.** The light follows the hour of the run (late May in western Germany: sunrise a little after five, sunset about half past nine, with twilight either side).
  - At night the sun and sky light dim and turn cool, the sky and fog go deep blue; at sunrise and sunset they turn warm. The rivers and the x-ray view darken with them.
  - The street lamps come on at dusk: their bulbs light up and the streets take on an even warm light (emission over the road tiles' own colours), so a lit street reads as one street, not a row of spots.
  - Before any run the town shows noon.
- **No glows in the air.** Alarm lamps, the mast's obstruction lamp and the spilling water are model geometry; the only remaining overlay is the radio link.
- **Models, not code.** Every structure, including the tank's section, the outfall and the bridge, is modelled in Blender; the code finds parts and anchors (where a label, an aerial or the water goes) by role.
