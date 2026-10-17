# Turf autotile normal maps — 2026-10-17

Normals were authored from the user's CURRENT 20 color overlays, including the user's expanded full-height grass foot. No earlier color-generation scripts were rerun. The draft and runtime normal atlas were updated only within the 20 native overlay source rectangles (1088 texels total). All color arrays and PNGs, three base normal textures, unused atlas slots and other blocks are unchanged.

Current palette colors are obtained from the actual DrawNormalPicker expression through `NormalizeTurfNormalPalette.ps1 -PaletteOnly`. Native normal vectors are snapped by angular similarity to those exact 12 RGBs; no intermediate colors or antialiasing remain. Matching base colors inherit their existing hand-painted base normals, then snap if necessary. Soil cracks are recessed, grey stones convex, and grass caps roll through up/45-up/outward directions. The grass foot is treated as continuation of the Top plane rather than a vertical soil wall. Independently painted corner variants use their own current color context.

Every overlay normal alpha equals its current color alpha exactly. There are 626 nonzero-alpha and 462 zero-alpha texels; invisible normals stay zero-alpha while their unused RGB is a valid neutral picker color. 808 normal texels changed.

Checks passed: exact palette membership, per-texel color/normal alpha in draft and both actual runtime PNGs, runtime/draft overlay parity, preservation of all color edits and other block data, whole-normal-PNG unchanged outside target areas, and existing shader normal-basis regression in all four camera directions. A second read-only `CompleteTurfAutotileNormals.ps1 -CheckOnly` run passed.

The older strict color-corner/straight-strip equality test currently fails on the user's manually changed color maps. It fails identically when pointed at the pre-normal backup using `TurfAutotileChecks.ps1 -DraftPath ... -SkipPreview`; this predates normal generation. Those color differences were intentionally preserved, not repaired or relaxed. No claim of a passing full color-corner regression or GPU image test is made.

Preview of actual packed color/normal data: `Docs/References/Turf-Autotile-Color-Normal-Preview.png`. Actual lighting should be assessed in Unity.

Reproduction is guarded against overwriting subsequent painting. Full-file backups are retained here; after later unrelated changes, selectively recover only Turf overlay normal pixels rather than restoring whole files.
