# Chunk borders debug

F9 toggles session-only chunk boundary highlighting in Game view, even with F3 hidden or simulation paused. F3 displays Chunk Borders: On/Off and the F9 hint. Defaults off on entering Play Mode; no PlayerPrefs or world-save changes.

The existing procedural terrain/water shaders tint only boundary strips after lighting. Cyan and amber distinguish the two chunk axes on tops; vertical cyan stripes continue at chunk sides. This is a surface grid, not a full-height wireframe box. Lines respect existing scene depth/occlusion and do not paint over the player's sprite. Fog or opaque geometry can conceal them. No new meshes/draw calls, CPU chunk rebuilds or light changes. Packed source-air cell coordinates keep the grid aligned with each owning 16x16 chunk across all camera directions and negative world chunks; line width uses screen derivatives for visibility while zooming.

Checks: actual shader coordinate arithmetic across all cells/four views; terrain/water HLSL variants; runtime compilation and F9/F3 wiring contracts. Actual key input and visual thickness need Unity Play Mode review.
