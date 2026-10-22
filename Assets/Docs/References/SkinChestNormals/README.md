# Local skin chest normal correction

Target: `Art/Player/Animations/Body/body_idle_normal_skin_draft.png`.

Only opaque skin pixels in top-row front-facing frames, source rows 34-47, are eligible. Face/beard rows above, legs, back, side views, every transparent pixel and all alpha remain unchanged. The second idle frame is shifted one pixel left relative to the first. This patch does not update the user's separately combined `body_idle_normal.png`: recompose the revised skin layer manually.

`chest_diffuse.png` and `chest_normals_before.png` are nearest-neighbour enlarged 28x16 source patches used as anatomical/edit references. The built-in imagegen patch is sampled to this grid, snapped to the exact block-editor normal palette, and composited strictly through the existing chest alpha by `PatchSkinChestNormals.ps1`. Before replacement the whole original skin PNG is backed up as `body_idle_normal_skin_before_chest.png`. In-game relighting remains a visual art check.

## Final built-in imagegen prompt

undefined
