# Pine instances and Rubedo

## Three different identities

- `StructureDefinitionId.Pine = 1` is the structure **type**. Rubedo stores this
  value together with `PlacementSelectionKind.Structure`. It is not an item ID.
- `ChunkTree.Id` is an **instance ID within its owning chunk**. Procedural
  candidates use 1–4, the isolated prototype uses a negative ID, and Rubedo
  allocates manual instance IDs starting at 1024. Identity is `(chunk, Id)`.
- `(Seed, GeneratorVersion)` selects a **procedural visual variant**, not an
  instance. Many trees share the same cached texture slices.

## Definition and rendering

There is currently no separate pine `ScriptableObject` definition or general
structure database. `PineTreeTest.Shape` is the shared pine configuration;
`Scale` and `QuarterTurn` control placement. `TreeResourcePreparationSystem`
prepares four seeds and four views for the current world/configuration.

`ChunkTree` stores the type, instance ID, seed/version, position, scale, turn,
trunk dimensions and manual/procedural provenance. It lives in a buffer on the
chunk containing its base, independently of terrain blocks. Collision uses the
trunk cylinder; the crown does not become terrain or a solid collider.

The renderer draws one vertical card per tree at the depth of its base. The
surface map remains necessary for lighting, local GPU clipping and fog height.
CPU occlusion masks are not built.

## Distant tree sun shadows

`TreeCoarseSunShadowSystem` supplements the detailed near-player sun cache.
Each pine uses one trunk capsule and three crown ellipsoids. A compute pass
bakes tree-only sunlight visibility into 2-unit world-space cells, over the full
0..256 height range of each loaded receiver chunk in a 9x9 neighborhood.
Terrain Tops/Sides and water sample this cache; cut caps remain untouched.
The terrain shader blends from the existing detailed result to the coarse
result over the last unit of the detailed radius, without multiplying two tree
shadows. The outer cache boundary fades over eight world units.

Only one chunk is dispatched per presentation frame, nearest pending first.
Tree/configuration/loading changes are scanned every 0.25 seconds; retained
chunks survive player chunk crossings. Sun snapshots are quantized to roughly
one degree and completed before scheduling a new direction, preventing queue
starvation. Existing cache results remain visible during refresh; newly loaded
chunks are unshadowed until built. This is asynchronous LOD, not a readiness
barrier for terrain or local projection. The visibility allocation is ~2.53 MiB,
plus the small chunk lookup and temporary four-proxy-per-caster buffer.

Sun rays have a 32-unit reach. Only loaded trees cast; this is not a world-wide
shadow solution. GPU Sun Lighting and the existing Detailed Shadows tree switch
control both LODs. Local point lights retain their current detailed radius.
Use the `TreeCoarseSun.BuildChunk` profiler marker to measure refresh cost.

## Rubedo

`Blocks`, `Structures`, `Items` select what the existing placement skill creates
on the next world click. Items come from the baked `ItemDefinitionReference`
catalog, preserving their actual definition IDs and starting at full durability.
The menu does not create replacement item definitions or consume the staff.
World-state version 9 adds saved Item/Structure selection kinds; older world
states remain readable. This saves the tool's selection, not placed trees.

Items and structures require a real selectable Top, not a Side or cut cap.
Half-height surfaces are supported. Pine placement checks trunk clearance,
player collision and overlap with existing trunks. The crown may overhang.

Manual trees have `PlacedByPlayer = true`. Procedural forest regeneration keeps
them and rebinds their seed to the rebuilt visual bank. This is runtime state:
manual-tree persistence across world unload/restart is not implemented here.

Nigredo uses an explicit `Item.Selection.Kind` branch in the skill graph.
Block/None routes through Behind Cursor to terrain removal. Item and Structure
route through Before Cursor to a category-specific lookup, then destruction of
the resolved entity or `(chunk entity, tree instance ID)`. The selected item ID
does not filter destruction; missing/stale objects never fall back to terrain.

To support other species, the next architectural step is a structure-definition
catalog (stable ID, generator/configuration, icon and collision rules), rather
than reusing an instance ID or adding more logic to `ItemDefinition`.
