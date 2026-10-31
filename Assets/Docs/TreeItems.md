# Fallen branches

Branch (item ID 8) and Large Branch (ID 9) are registered in ItemDatabase,
available in Rubedo, equippable in either hand and use the existing Throw skill.
No crafting or ignition recipe is added yet. Both start at 100 durability without
passive decay. Normal world-item pickup, lighting, rendering and streaming apply.

After natural tree population, TreeItemSpawnSystem processes at most one new
chunk per frame. A deterministic world-seed/tree-ID hash selects 1–2 small
branches and a 1/3 chance of one large branch per natural tree. Each has up to
12 placement attempts, approximately 1.4–3 units from the tree. Placement stays
within the owning chunk, accepts sky-exposed turf/sand/stone, rejects water,
large height differences, occupied candidate cells and tree trunks. A tree can
produce fewer items if no safe surface exists. Manually placed trees are excluded.

TreeItemSpawnedChunk records are saved in world-state version 10. Existing
versions remain readable and get a first forest-litter pass when visited.
Collected litter does not regenerate on stream-in or a later session. Actual
items are saved by the existing WorldItemStreamingSystem, including inactive
items; tree structures themselves still need not be persisted.

## Generated artwork

Built-in imagegen was used for both transparent raster assets:

- Art/UI/Icons/branch.png
- Art/UI/Icons/large_branch.png
- Art/UI/Icons/branch_world.png
- Art/UI/Icons/large_branch_world.png

The original generated alpha images are preserved outside the project. The actual
project PNG files are fitted with nearest-neighbour sampling to 32x32 UI icons
and separate 32x16 ground sprites. Unity imports them as single sprites with
point filtering and no mipmaps/compression. Import-size limits alone are not a
substitute for correct source sprite dimensions: ground rendering uses sprite.rect/32.

Prompt shared by both generations:

> Use case: stylized-concept. Asset type: production transparent pixel-art item
> sprite for a dark muted medieval top-down 2.5D survival game. Actual alpha
> transparency, no background, no ground plane, no cast shadow, no text, no
> border. One isolated fallen dry pine branch, laid diagonally lower-left to
> upper-right, viewed obliquely from above. Crisp low-resolution hand-pixelled
> look, limited desaturated brown bark, grey-brown highlights, near-black outline,
> no leaves/needles, no glow, no antialiasing. Center and fit entire object with
> small transparent padding. Square canvas. Intended final display about 24–40
> pixels across, keep silhouette simple and readable.

Small-branch suffix:

> Small Branch: a slender short crooked stick with one little fork near the
> upper end, not a log. Single item.

Large-branch suffix:

> Large Branch: a noticeably thicker heavy broken bough with two short snapped
> offshoots and a pale broken wood end; not a bundle or tree trunk. Single item.
