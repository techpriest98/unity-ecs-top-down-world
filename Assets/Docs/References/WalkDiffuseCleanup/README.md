# Walk diffuse cleanup

Updated body_walk_diffuse_clean.png in place. Original preserved as body_walk_diffuse_clean_before.png in this folder. PNG metadata/GUID unchanged.

First three top-row frames remain byte-for-byte identical at decoded RGBA level. Remaining thirteen frames have warm cloth ramps flattened to #817D6A and skin ramps to #A2968B, the dominant example colors. Existing grayscale hair/beard/ink, transparent pixels, alpha and pose silhouettes are unchanged. Minor noncanonical color variation in the authored three frames is deliberately preserved.

Built-in imagegen was used for a cleanup proposal, but its redrawn sheet is not substituted for source geometry. Final pixels use deterministic material-ramp remapping by CleanWalkDiffuseFromExamples.ps1, avoiding generated resampling or hair changes. The script validates the saved PNG against its in-memory result and source alpha. No animation or runtime references changed.

Raw proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-9d0438fd-a2e6-4c50-a0cd-b40b081ef57f.png

Prompt:

Edit this 256x512 pixel art sprite sheet. First three cells of top row are authoritative examples of clean diffuse albedo: flat olive clothing and flat warm grey skin, with existing hair retained. Flatten clothing/skin shading in the remaining thirteen cells in exactly that style. Preserve first three cells unchanged. Preserve all hair shading, beard/eye materials, silhouettes, pixel positions, black outlines, belt and transparent background. Four columns by four rows, 64x128 cells. No pose or geometry changes. Cloth base #817D6A, skin base #A2968B; do not alter hair. No gradients, no antialiasing, no new details. Transparent outside characters.
