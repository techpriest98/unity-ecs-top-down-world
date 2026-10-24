# One-tree runtime prototype

WorldScene/ChunkRenderer has a PineTreeTest component. Disable it to remove generated trees.
`Populate Turf` is now enabled: up to four spaced candidates per 16x16 chunk, with configurable
`Turf Density` (default .65). Candidates are accepted only on sky-visible Turf surface samples,
including half-height turf on slopes; no sand, rock or water. A two-block spawn clearance is reserved.
World seed + chunk/cell coordinates determine placement; four shared shape seeds, quarter turns and
uniform scales provide variation. Owner chunk reload reconstructs the same population.
Density/shape edits rebuild loaded population. This is not yet a persistent editable forest.
Turn Populate Turf off to restore the original single nearby test tree behavior below.
After startup reaches Ready it finds a dry surface 3–5 blocks near the player, at a comparable height.
It adds a ChunkTree entry to the anchor's generated chunk, never writes BlockData and never suppresses
the terrain projection. The record contains ID, seed, generator version, position, quarter turn,
scale, and a trunk-only vertical cylinder. Loaded neighboring chunks participate in player collision.
The one test record is remembered for the current play session and reattached if its owner chunk reloads.

The pure 3D generator now lives in Scripts/World/Trees. The Workshop still uses the same generator.
A background CPU task rasterizes four diffuse/normal/physical-surface views once per settings key.
Unity texture uploads occur on the main thread only after task completion. No PNGs/assets are generated.
This is an experimental CPU bake, not GPU baking; the first appearance can take several seconds.
No many-tree stress performance claim is made. Masks are keyed by owner entity plus tree ID,
so several trees in a chunk cannot overwrite one another. Trees outside the main camera are culled.

TreeSprite uses the shared detailed local-light cache, generated normals and active sunlight.
Per-pixel height/depth support terrain occlusion and choosing front/behind the player's -1 depth plane.
Transparent needle pixels never occlude the background. Terrain occupancy is not changed by a tree.
Only the trunk blocks the player; branches and foliage remain passable. Item/projectile collision is unchanged.

Not implemented: save-file integration, resources, tree-cast skylight occlusion,
GPU sprite baking, tree fog-height stencil splitting and an Entities Graphics renderer.
The current ChunkTree data is a prototype record, not a promise of an established save-file schema.
In Play Mode check all four camera views, walking/jumping around the trunk, foliage traversal,
player/rock overlap, local lighting, owner chunk unloading/reloading and disabling PineTreeTest.

## Batched GPU rendering

Four shapes share diffuse, normal and physical-surface Texture2DArray banks (16 slices each).
Each frame selects the shape/view slice per tree in a 64-byte TreeDrawData structured-buffer record.
Up to 32 ready, camera-visible trees share one procedural instanced draw and one R8 mask array.
Tree masks are cached CPU byte arrays rather than separate GPU textures. Only dirty/remapped mask
slices are staged, and the array Apply is skipped entirely when a batch's masks have not changed.
F3 displays Trees (submitted instances) and draws (submitted tree batches); e.g. 33 instances use two draws.
This uses Graphics.RenderPrimitives GPU instancing, not a migration to Entities Graphics/DOTS materials.
CPU bark/foliage baking and CPU terrain-mask generation remain separate costs; batching does not remove them.

## Near-player tree shadows

`PineTreeTest → Detailed Shadows` toggles shared local/GPU-sun shadow casting.
The deterministic generator supplies capsule proxies for trunk/branches and separate small ellipsoids
for terminal needle sprays. A shadow-only generation path consumes identical random choices but builds no triangles.
These are approximated opaque foliage patches, not per-needle alpha or translucent foliage tracing.
The proxies are spatially indexed into the existing near-player occupancy grid without modifying BlockData,
physics colliders or CPU skylight/sun calculations. Exact analytic intersections avoid widening a trunk to one grid cell.
The existing visibility/edge caches include this field; tree changes invalidate them by actual content hash.
Source/receiver cells also test tree proxies, and a receiver inside its own proxy is ignored to avoid black bark/foliage.
Directional shadows require the existing GPU Sun Lighting option. CPU voxel sunlight is unchanged.
Outside the configured detailed radius the old lighting/fade remains. Measure GPU cost in Play Mode with the toggle.
