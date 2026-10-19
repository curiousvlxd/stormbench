# 0013. The HMI on the open-source Siemens iX design system

Status: accepted, 2026-10-15; superseded 2026-10-19 by the StormBench design system (see Amendment). Replaced the palette and typeface choices of ADR 0010; the ISA-101 rules of the process display stay.

## Context

The first HMI had hand-made controls and Atkinson Hyperlegible Next, whose slashed zeros made the screen look like a code editor. Industrial software is built on design systems. In October 2026 the only open one with industrial components and a Blazor wrapper was Siemens iX (MIT, `Siemens.IX.Blazor` 1.0.0, maintained under the siemens GitHub organisation, marked experimental by the iX documentation).

## Decision

- **iX classic theme, light and dark** (`data-ix-theme`, `data-ix-color-schema`). The console uses iX components (select, radio group, toggle buttons for the speed, buttons); the alarm banner and list use iX buttons. Surfaces, text and borders of the whole page take iX tokens, so both schemas follow iX.
- **No Siemens brand.** No logo, no brand theme, no Siemens Sans (a brand font outside the MIT licence). The page keeps its own name, and NOTICE states that the project is not affiliated with Siemens.
- **Typeface Source Sans 3** (Adobe, SIL OFL 1.1), served with the page; the iX components use it as well.
- **The process display keeps ISA-101:** grey process, blue water, alarm priorities 1 to 3 in red, orange and yellow with shape and number. iX provides alarm red and warning yellow; the orange of priority 2 is our own.
- **No prerendering.** The iX web components rearrange their markup on load, which broke the hand-over from a prerendered page to the interactive renderer ("error applying batch"). The HMI renders interactively only.

## Consequences

- The screen reads as industrial software and matches what German automation teams know.
- One more dependency, young and outside Siemens' own release process. Its components are web components, so a later swap stays local to the console and buttons.
- Headless screenshots must wait for the components to load (`ix-select.hydrated`).
- The charts and animation in `results/` use Atkinson Hyperlegible Next until they are rendered again.

## Amendment, 2026-10-19: the StormBench design system

Next to the Live 3D view and the Benchmarks page, the operator display looked like a different product: grey, square, iX's own controls. iX is removed. The whole app now uses one design system: soft blue-grey ground, white cards with rounded corners and soft shadows, one blue accent (`#0a84d6`) and Source Sans 3, the same tokens as the Live 3D view. The operator display keeps the ISA-101 rules (grey-scale process display, colour only for water and alarms, ISA alarm shapes per priority).

- **Light by default, dark on request.** The theme is the user's choice (header toggle, stored in the browser), not the operating system's: a control room screen should not change when a laptop switches to dark mode.
- **Native controls.** The console's select, radio cards, segmented speed control and buttons are plain HTML styled with the tokens: nothing to load before the page works, no prerendering workaround.
- **Layout.** Key figures across the top, network and console side by side, trend under the network, alarms at the bottom, so no column ends in an empty gap.
- **Labels in the network display** are placed so that covering a symbol or other text costs far more than crossing a pipe; each candidate is also tried further out. A test on the real Astlingen network checks that no label or alarm mark overlaps another.
- **Symbols in the same style.** A tank is drawn as the StormBench mark: a vessel with rounded corners and a lid, the water inside with a gentle wave on its surface. An overflowing vessel gets a soft, breathing halo, a coral outline and lid, and a round badge with a drop on its top corner; no arrows or streams. The palette is pastel: soft blue water with a gradient, slate outlines, pale pipes on a dotted canvas, alarm colours softened but kept apart by shape and number. Overflow structures and the treatment plant have the same rounded corners. ISA-101 stays: vessels are grey, only water and alarms carry colour, and an overflowing vessel's outline and lid turn priority 1 red.
