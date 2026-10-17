# Turf grass foot overlay — 2026-10-17

The user identified the soil strip appearing over the bottom of a rear wall, supplied by the foreground Turf block. This is the foot material's Side/Bottom overlay, not the rear wall's own material.

`TurfGrassFootOverlay.ps1` directly copies the main Turf Top texture's first four (back) rows into the 53 visible Side/Bottom strip texels, preserving the existing alpha profile. The two bottom corner intersections use the same copied grass wherever the foot strip is visible. Original lateral dirt art remains outside that grass footprint. No generative image operation was needed: this is a native pixel transfer from existing art.

Top, SideUpper, SideLower and all unrelated overlays, normal maps, other blocks and all alpha bytes are unchanged. Draft and runtime color PNG were updated together and verified, including every pixel outside the changed area.

Production Compose checks passed all 1536 neighbour/above/foot cases. The foot-corner check now correctly gives the foreground grass footprint priority over the lateral dirt rim at intersections, while retaining strict strip/corner equality elsewhere. Preview `Docs/References/Turf-Autotile-Color-Preview.png` now includes Stone with a Turf grass foot (the user's reported case). In-game lighting remains a visual check.

Full-file backups are retained here. Recover only relevant Turf color pixels if later unrelated edits exist.
