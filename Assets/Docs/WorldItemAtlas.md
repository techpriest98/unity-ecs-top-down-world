# World item sprite atlas

## Manual build

Select ItemDatabase in Project, then use **Assets > Game > Build World Item Atlas**
(the same workflow as Build Animation Texture Array), or **Build World Item Atlas**
in its Inspector. Stop Play Mode first. Each build creates a unique
`<database>_WorldAtlas.asset` beside the database, with its texture embedded as a
subasset. Assign it to **Item Library Authoring > Atlas**. The atlas references its
Source Database, so the authoring component does not need a separate database field.
Previous assets are preserved. Rebuild after editing world sprites or changing the
database list, then assign the new atlas to the authoring component.
No Read/Write import setting is required: only this editor operation reads the
completed GPU atlas back once to serialize its texture.

The baker tracks the assigned atlas and texture and stores WorldItemAtlasReference.
When assigned, the saved texture and UV metadata bypass runtime GPU packing.
Missing entries fall back to their original sprite texture until the atlas is
rebuilt. Missing atlas/texture/Source Database is an explicit baking error.

`WorldItemSpriteAtlas` consumes the baked `ItemDefinitionReference` buffer produced
from `ItemDatabase.Items`. Only `WorldSprite` rectangles are included; HUD icons
and original sprite import settings are unchanged.

Unity may return a trimmed `textureRect` smaller than the sprite's logical
`rect` (for example 10x58 inside a 32x64 torch canvas). Atlas UVs address this
trimmed image. The renderer draws a correspondingly trimmed quad, offsets it by
`textureRectOffset`, and retains 32 source pixels per world unit instead of
stretching the crop to fill the canvas. Grounded sprites compensate anchor depth
for the changed quad centre to stay on their original Top plane. Saved atlas
entries whose crop dimensions no longer match the source fall back to source
textures with a rebuild warning.

The shared editor packing implementation uses integer shelf packing, two transparent padding pixels,
point sampling, no mipmaps, no resizing. GPU Blit crops/converts each source sprite
into a same-sized temporary render texture, then GPU CopyTexture copies that tile
into the shared sRGB render texture. This works without source Read/Write imports
or CPU readback. Duplicate Sprite references share a slot. The maximum atlas side
is 4096 (or the platform limit if smaller). Unsupported/capacity failures warn and
fall back to original sprite texture batches.

The renderer already uses DOTS entities and structured-buffer procedural GPU
instancing, not one GameObject/renderer per item. The atlas removes texture-run
batch breaks. Sprite ID lookup is cached rather than scanning the library per item.
Consecutive item ordering, 128-instance pages, above/below-fog stencil splits,
lighting, local projection coverage, and physical depth are preserved. With one
fog class, N visible sprites need ceil(N / 128) submissions regardless of item type.
Alternating fog states still split batches to retain equal-depth/stencil ordering.

The atlas is reused until the library/sprite/texture signature changes or its GPU
resource is lost. Build time appears as `WorldItems.BuildSpriteAtlas`; steady-state
submission stays under `WorldItems.SubmitBatches`. Resources are released when the
render system stops or is destroyed. Adding an item with a WorldSprite to the
database and rebaking is enough for the automatic path. Assigned manual atlases
must be rebuilt through the menu/button.

Validation: run WorldItemAtlasChecks.ps1, WorldItemBatchChecks.ps1 and
WorldItemRenderChecks.ps1. Actual GPU appearance and draw-call savings should be
checked in Unity Frame Debugger with mixed item types, both fog classes, thrown
items, local projection movement and a scene reload.
