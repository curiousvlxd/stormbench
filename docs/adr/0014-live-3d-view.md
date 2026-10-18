# 0014. A Live 3D view for visitors, next to the operator display

Status: accepted, 2026-10-15. The operator display of ADR 0010 and ADR 0013 moves to `/operator` and stays as it is.

## Context

The operator display is built for someone who knows SCADA: ISA-101 symbols, alarm lists, EFD, Base Case. Shown to a visitor or in a demo, it did not explain itself. The page was taller than a laptop screen, the key result sat in a small tile, and nothing said in plain words what was happening or why it mattered. The testbed needs a view that a non-specialist understands in a minute, that shows the same live data, and that can be presented.

## Decision

- **A second view at `/`: the town in a storm, in 3D** (three.js r180, MIT, served with the page from `wwwroot/lib/three`, no CDN). It is built from `Model/Network` like the operator display: tanks as cutaway vessels with their live level, controlled gates whose plate shows the real opening and an outline the commanded one, overflow structures, the treatment plant, the field controller (PLC) with its cables to the gates, and the control room (SCADA) with the link to the PLC as an arc. While a point spills its channel runs brown and a plume grows in the receiving water with the volume spilled there. Rain falls as hard as the simulator reports.
- **The benchmark's own map, nothing made up.** Node positions and the ten subcatchment polygons come from the `.inp`, the rain gauges RG1 to RG4 from its symbols; Main River, Park Creek, the park and the place names are traced at run time from `astingen.jpg`, the model's georeferenced backdrop (`stormbench_sim/backdrop.py`), and each subcatchment takes the backdrop's tint. Houses stand only inside the subcatchments, the paved areas that drain into the sewers, with the town-centre buildings around "Town Centre". Every overflow point drains to the nearest receiving water, river or creek. The only liberty: tanks, drawn far larger than to scale, and chambers next to them move just clear of the water's edge or of each other; their pipes follow.
- **Low-poly, sunny, one family of models.** Houses, town-centre and industrial buildings and trees are Kenney's CC0 kits (instanced, a few draw calls); tanks, gates, chambers, pipes and the plant are built in code (`live/models.js`) in the same flat-shaded palette, with the industrial kit's orange band on the tanks. A clear sky that turns overcast and grey as it rains; soft sun shadows, ambient occlusion (GTAO), SMAA; light glass panels over the scene.
- **Live versus illustration, stated on the page.** Levels, gate positions, overflows, volumes, rain, the link state and the PLC's fallback are live from the UNS. The houses, the river's course and the flow animation in the pipes are illustration; "How it works" says so.
- **One screen, little text.** Over the scene: a scoreboard with two bars (this run, and the same storm with no control) and the difference, a card for the clicked structure with one sentence, at most two short status lines, and a dock with storm, control, Start and three faults ("Cut link", "Jam gate", "Freeze sensor"). Speed stays at the default; the operator display offers the others. Gate labels appear only when a gate is stuck or selected.
- **Plain words.** T4 is Tank 4, CSO7 is Overflow 7, V2 is Gate 2; Base Case is "No control", EFD is "Smart". The status line (`LiveStory`) states the most important thing first: a cut link and the PLC carrying on alone, a jammed gate or frozen sensor and whether the SCADA has caught it yet, what spills, the rain, and at the end the result.
- **Same modules, same commands.** The page reads `HmiFeed` and sends the same console commands as the operator display; it adds no metric and no endpoint. A frame (`LiveFrame`) goes to the browser only when a module reports a change, at most ten times a second.
- **Always light.** A sunny scene reads best for a visitor; the operator display keeps its light and dark themes.

## Consequences

- A visitor sees in one picture what the testbed does and what smart control is worth; the operator display stays the place for alarm handling and the ISA-101 symbols.
- One more vendored library (about 1 MB of JavaScript with the addons for controls, post-processing, rounded boxes and geometry merging). Its addons import `three` by bare name; the copies rewrite that one import to a relative path, nothing else is changed.
- The scene needs WebGL. Headless screenshots run in Edge with the GPU enabled.
- Faults injected on an empty tank show a real limit of the frozen-sensor check: an empty reading that stays put looks like a dry tank. The status line says so, and the "Freeze sensor" button picks a tank that is filling.
