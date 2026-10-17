# Rounded grass rim — 2026-10-17

Snapshot before replacing only connected grass-cap side normals of Turf (ID 9). Upper-side grass now turns from near-up through 45-up to near-outward: 15 / 45 / 65 / 80 degrees from up down each connected column. The cap depth follows grass/straw colors, stopping at soil gaps rather than treating dangling roots as a full rim. Slight existing horizontal blade variation is retained.

104 upper-side pixels changed. Existing top normals (including enhanced blades), soil, stones, transparent overlays, lower side, all colors and all other blocks are unchanged. Leaving the base top untouched also avoids introducing a bevel stripe at every repeated top tile. Color texture and geometry are not modified; this is normal-only rounding.

Verified normalized vectors, alpha coverage, native draft/runtime PNG equality on changed pixels, untouched data elsewhere, and 15/45-degree stages across all 32 seam columns. In-game visual appearance remains to be assessed.

Reproduction: `Tools/BlockAtlasEditor/Editor/CompleteTurfNormals.ps1 -GrassEdge`. Backup guard protects future painting. These backups contain whole files: after later edits, selectively recover the ID 9 normals instead of overwriting unrelated work.
