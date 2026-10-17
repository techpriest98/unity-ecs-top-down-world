# Turf top: stronger blade facets — 2026-10-17

Snapshot immediately before strengthening only the 32x16 top normal rectangle of Turf (ID 9, atlas column/row 0/0).

131 straw/light-olive pixels identified from the existing color palette now have approximately 39–51 degree slopes. Their horizontal directions follow local material relief and exposed edges, with wrapping at top-tile boundaries. Broad patch interiors and the existing substrate remain unchanged. This is an approximate interpretation of grass blades, not a claim of semantic image recognition.

The native draft and runtime normal atlas were updated together. Color data, side normals (including hand-painted stones), overlays and every other block are unchanged. Checks cover unit vector length, alpha coverage, unchanged non-target pixels and PNG/draft parity within the edited rectangle.

Reproduction: `Tools/BlockAtlasEditor/Editor/CompleteTurfNormals.ps1 -GrassTop`. The backup guard prevents repeated application over subsequent painting.

These are full-file backups. After later edits, recover only the ID 9 top normal rectangle rather than restoring whole files over newer work. Visual lighting still needs evaluation in Unity.
