# 0013. The HMI on the open-source Siemens iX design system

Status: accepted, 2026-10-15. Replaces the palette and typeface choices of ADR 0010; the ISA-101 rules of the process display stay.

## Context

The first HMI had hand-made controls and Atkinson Hyperlegible Next, whose slashed zeros made the screen look like a code editor. Industrial software is built on design systems; the only open one with industrial components and a Blazor wrapper found in October 2026 is Siemens iX (MIT, `Siemens.IX.Blazor` 1.0.0, maintained under the siemens GitHub organisation, marked experimental by the iX documentation).

## Decision

- **iX classic theme, light and dark** (`data-ix-theme`, `data-ix-color-schema`). The console uses iX components: select, radio group, toggle buttons for the speed, buttons; the alarm banner and list use iX buttons. Surfaces, text and borders of the whole page take iX tokens, so both schemas follow iX.
- **No Siemens brand.** No logo, no brand theme, no Siemens Sans (a brand font outside the MIT licence). The page keeps its own name, and NOTICE says the project is not affiliated with Siemens.
- **Typeface Source Sans 3** (Adobe, SIL OFL 1.1), served with the page; the iX components are pointed at it as well.
- **The process display keeps ISA-101:** grey process, blue water, alarm priorities 1 to 3 in red, orange and yellow with shape and number. iX provides alarm red and warning yellow; the orange of priority 2 stays ours.
- **No prerendering.** The iX web components rearrange their markup when they load, which the hand-over from a prerendered page to the interactive renderer did not survive ("error applying batch"). The HMI renders interactively only.

## Consequences

- The screen reads as industrial software and matches what German automation teams know.
- One more dependency, young and outside Siemens' own release process; its components are web components, so a later swap stays local to the console and buttons.
- Headless screenshots must wait for the components to load (`ix-select.hydrated`).
- The charts and animation in `results/` still use Atkinson Hyperlegible Next until they are rendered again.
