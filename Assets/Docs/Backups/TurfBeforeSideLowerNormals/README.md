# Turf SideLower normals — 2026-10-17

Numeric edit of the native Color32 vector data for Turf ID 9, SideLower rectangle x=0..31, y=32..47. 453 normal pixels changed in the draft and runtime normal atlas. All color data, Top/SideUpper normals, overlays, and every other block are unchanged.

Soil palette classes define recessed cracks and raised grey stones rather than generic luminance relief. Stone interiors have a small dome over their bevels; broad soil interiors remain comparatively flat. The first lower-side row inherits the authored SideUpper normal where its color is continued, preserving the boundary without changing SideUpper. X wraps for neighbour sampling; the bottom samples clamp rather than assuming an unpainted continuation.

Checks: normalized vectors, alpha coverage, protected draft data, unchanged atlas pixels elsewhere, exact draft/PNG parity. Actual stone bevels passed sign checks at 11 left, 11 right, 14 top and 14 bottom positions; all 32 boundary normals matched. Shader normal basis regression passed all four camera directions. Actual GPU lighting remains to be visually assessed in Unity.

Reproduction: `Tools/BlockAtlasEditor/Editor/CompleteTurfNormals.ps1 -SideLower`. The existing-backup guard protects later manual painting. Full-file backups are retained here; after later changes, selectively recover only ID 9 SideLower rather than overwriting whole files.

Preview of final color and normal data: `Docs/References/Turf-Side-Color-Normal-Preview.png`.
