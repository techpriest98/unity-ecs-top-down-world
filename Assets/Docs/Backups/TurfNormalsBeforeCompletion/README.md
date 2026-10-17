# Turf normal completion — 2026-10-17

Before-images of the complete draft and runtime normal atlas. Turf retains numeric BlockId 9 (previously MeadowGrass); saves and atlas coordinates remain unchanged.

Only Turf's normal pixels were edited. Original color arrays and all other block entries/atlas rectangles are unchanged. 75 hand-painted directional side pixels were retained. Large uniform center-color fills were treated as background. Material-specific shallow relief was added to the painted top and upper side (grass/straw, soil cracks, grey stones); this is not a generic luminance-to-height filter.

Unpainted transparent autotile overlays retain no visible normal coverage. The lower side's unfinished checker placeholder stays flat rather than gaining fake checker relief. Future color painting there still needs corresponding authored normals.

`Tools/BlockAtlasEditor/Editor/CompleteTurfNormals.ps1` performs the numeric native-data conversion and verifies coverage, vector length, PNG/draft equality, and unrelated pixels. It intentionally refuses to rerun while this backup exists, to protect subsequent hand painting.

These are full-file backups. Do not restore them wholesale after later changes to other blocks; selectively recover only the ID 9 entry and its 64x48 top-left normal atlas rectangle instead.
