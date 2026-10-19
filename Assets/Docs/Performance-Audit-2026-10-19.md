# Performance audit — 2026-10-19

## Scope and confidence

Read-only source audit of item physics/spawn/render/light/ignition, terrain streaming/generation/projection/render uploads, lighting, audio, player, HUD/minimap and persistence. Two independent read-only reviewers checked terrain/render/light and item/player/UI paths. No gameplay changes were made. No live Unity Profiler or GPU capture was available. Findings below are confirmed code paths and unmeasured bottleneck hypotheses, not measured system timings.

User's Editor observations: static ~250 → ~200 FPS (4 → 5 ms); moving ~160 → ~90–100 FPS (6.25 → 10–11.11 ms). Additional moving-frame cost is ~3.75–4.86 ms, compared with ~1 ms static, provided conditions are comparable. Need to hold seed, position, camera/zoom, lights, resolution and power mode fixed before attributing a regression.

The available player log contains a successful coastal spawn and no repeated exceptions in its inspected tail. It also contains a ComputeBuffer cleanup warning on shutdown; source ownership has not been traced, so this is not proof of a frame-time bottleneck. The available saved world has 94 world items (42 Flint, 50 Pyrite, 1 shard, 1 torch) and 159 processed coastal chunks. These are saved counts, not live profiler counters or evidence that 94 items alone explain the regression.

## Prioritized findings

### First projection-cost patch

**Rolled back after the user's Unity capture:** ChunkProjection.Build measured 44.21 ms in the new selected frame (system 44.34 ms), worse than the previous 17.50 ms system frame. These are not matched averages, but the patch is not retained as a demonstrated improvement. Restored original builder (removed empty-air early path) and temporary hash-map occupancy, including its allocation/disposal policy. Kept ChunkProjection.Build instrumentation. Baseline comparisons now assert exact geometry/water/clip outputs and identical accessor counts. Runtime compilation and regression checks pass. No Burst error was found in the inspected Editor.log; this does not establish why performance worsened. A fresh same-route capture after rollback is required before another isolated projection change. Successful item occlusion changes were not reverted.

Inspection found near-player cell changes intentionally mark projection-clipped chunks dirty in ChunkClippingSystem: hiding geometry depends on player position/height, so those triggers were not simply removed. No clipping radius, movement quantization, projection budget, SkyLight or lighting invalidation rules were changed.

ChunkProjectionBuilder.ProcessCell now exits early for provably empty legacy air whose below and behind cells are also empty. Split references are explicitly excluded even if their low material byte encodes Air. The early path still calls RecordClippedVolume in the same traversal order, because clipped air contributes to masks used by distant shader-clipped chunks. Current/below/behind solid, fluid, half and split cells retain existing geometry paths. This avoids repeated opaque/water neighbour processing in empty sky/cave air without truncating world height or losing masks.

ProjectionOccupancy now indexes a dense 2D lattice rather than a NativeParallelHashMap sized to the voxel volume. Current dimensions: 16×528=8,448 uint entries per opaque/water occupancy (~33 KiB each). An occupied flag distinguishes a zero-valued Top/source-0 record from an empty entry; source/half metadata and first-writer priority are preserved. The two arrays are persistent per ChunkProjectionSystem, cleared between builds and disposed on system destruction, avoiding repeated large occupancy allocation/free. Public occupancy operations reject out-of-domain lattice coordinates; all generated coordinates fit the domain. Existing capacity constructor argument is retained for compatibility, not used as the lattice size.

Added ChunkProjection.Build marker around the geometry build. HalfBlockProjectionChecks compiles the captured previous builder alongside current code, comparing exact opaque, water and clipped-volume record order across four views, clipping on/off and mixed halves/chunk boundaries. New tests also exercise 20,000 random occupancy operations against a dictionary reference, empty reset and zero-valued occupancy. All existing terrain/projection/water/collision/save validation checks pass in the headless buffer/terrain adapters; runtime C# compilation passes. Actual Unity/Burst timing, rendering and movement acceptance remain pending. Compare the same route's ChunkProjectionSystem / ChunkProjection.Build with the prior 17.50 ms frame; do not assume average speedup from source inspection.

### Measured frame and first occlusion patch

User Profiler screenshots later identify a selected slow frame: ChunkProjectionSystem 17.50 ms, SkyLightSystem 12.11 ms, WorldItemRenderSystem/WorldItems.Render 8.02 ms with 1.3 MB and 28,628 allocation events; SubmitBatches .07 ms and BuildLightIndex .03 ms. These timings describe that captured frame, not an average or proof of persistent per-frame work. Main next targets are projection/SkyLight and item occlusion, not further submission tuning.

Occlusion patch replaces Dictionary<int2,List<Face>> with Dictionary<int2,int> heads and one reusable List<Face> storing linked indices. Rebuild clears/reuses containers rather than allocating a list/backing array for each projected tile. Reversing each bucket's traversal is safe because visibility is an any-occluder predicate, not an order-dependent blend. Global invalidation, hash rules, mask pixels, clipping and world/physics behavior remain unchanged. Capacity growth can still allocate when a new peak is reached; this is not a guarantee of zero allocations in the entire item renderer.

Added markers WorldItems.Occlusion.HashChunk (actual hash scans, excluding cached returns), WorldItems.Occlusion.Rebuild (clipped/face index rebuild), and WorldItems.Occlusion.BuildMask (pixel generation, excluding cached masks). WorldItemOcclusionChecks compares current production cache with a captured pre-change baseline using real projection/math helpers and terrain/texture/profiling adapters: pixels match across four views, clipped/unclipped/full/half/cut-face fixtures. Ten warmed rebuilds allocate 0 B in the flat implementation versus 2,671,520 B in the baseline under the test harness. Runtime compilation, batching and render-index regressions pass. Actual Unity timing/GC improvement remains to be captured. Projection and SkyLight were not modified in this patch.

### 1. Item rendering repeats expensive CPU work even for sleeping/off-screen items

`Scripts/Items/WorldItemRenderSystem.cs:52` clears lightingTiles each frame. At line 132, ApplyGroundLighting linearly searches projected cells of the item's supporting chunk for every uncached support tile. Missing results are not cached. At line 109, each drawable item gets a separate Graphics.RenderPrimitives submission. No camera/distance rejection precedes definition lookup, occlusion work or light lookup (engine render-bounds culling may occur later).

Cost grows approximately with all items plus distinct support tiles × projected records per chunk, not just visible items. Sleep fixes motion only. First changes to validate: early camera/loaded-area rejection, revision-aware per-chunk top-surface lighting index, negative-result cache, then grouped/instanced submissions. Lighting indices must refresh on light changes as well as geometry changes.

### 2. A local terrain/clipping change invalidates global item occlusion

`Scripts/Items/WorldItemOcclusionCache.cs:63` uses a global revision. A rebuild clears all face lists; line 101 creates new managed List<Face> instances per occupied projected key. Each grounded item's mask is regenerated per pixel and uploaded separately with Texture2D.Apply at line 167 after revision changes. Buffer changes due only to lighting can force geometry rehashing even when hashes later prove unchanged.

This is a strong movement-regression candidate: clipping/streaming changes can repeat it while standing still reuses masks. Validate rebuild count, face count, masks rebuilt, upload count and GC allocations. Prefer geometry-specific revisions and spatially bounded invalidation; avoid globally regenerating all masks after one chunk changes. Preserve occlusion correctness around cliffs/caves/half blocks/view rotations.

### 3. World items remain resident after terrain streaming unload

`Scripts/World/Chunks/ChunkStreamingSystem.cs:200` destroys only the chunk entity. WorldItem entities created by CoastalItemSpawnSystem/WorldItemEntityUtility have no linked chunk ownership; no matching item-unload path was found. `Scripts/World/Saving/Systems/WorldStateSystem.cs:145` restores every saved item, including distant ones. Item counts therefore grow with explored locations unless picked up or destroyed.

Render, light, durability and ignition systems still visit these entities. Runtime sleep/terrain dictionaries and occlusion chunk-hash dictionaries also retain historical entries until a later cleanup/disposal. Proposed durable fix: per-chunk item storage with bounded active ECS residency, explicit ownership migration for thrown items, and pickup/save ledger correctness. Do not simply destroy distant items: that loses dropped objects or creates respawn exploits. Early render culling is the lower-risk first step.

### 4. Movement can trigger a full terrain render snapshot/upload

`Scripts/World/Rendering/ChunkProceduralRenderSystem.cs:198` has an existing ChunkRender.FullSnapshot marker. Full rebuild iterates all generated projected records and uploads opaque/water/clipped layers. Center changes bypass lighting-only range updates (`:192`). `Scripts/World/Rendering/ChunkProceduralRenderer.cs:123` clears the full clip-mask texture then uploads it via Apply.

Existing good gates: static unchanged frames return; pending projection/lighting publication is coordinated; light-only updates have a partial-range path. Remaining cost is global snapshot/clip-mask work during movement. Measure FullSnapshot frequency, records/bytes uploaded, texture dimensions and Apply/SetData time. Incremental chunk ranges and a stable/dirty-region clip mask are candidates, but changing publication must not reintroduce clipping flashes.

### 5. Moving torch lighting is CPU-heavy and amplifies projected-buffer updates

`Scripts/World/Lighting/Systems/DynamicLightSystem.cs:71` builds temporary native collections and scans chunks/sources before unchanged early-out. Movement across a voxel cell triggers changed-region propagation; radius cubes have per-target voxel visibility walks. Chunk membership changes trigger a full rebuild. `ChunkLocalLightUpdateSystem.cs:26` expands dirty chunks to a 3×3 neighborhood and updates all affected projected records without a per-frame budget.

Existing gates: quantized light-source cell, changed bounds, radius cap 8, no-change early-out, direction cache. Burst alone does not schedule these main-thread traversals onto workers. Compare movement with/without a lit torch before deciding on GPU transfer. Candidate changes: smaller dirty regions, cached world membership, parallel jobs/Burst where safe, then GPU lighting only if measurements justify the architecture change.

### 6. Streaming does avoidable idle work; generation/projection remain synchronous

`Scripts/World/Chunks/ChunkStreamingSystem.cs:250` enumerates every square for each ring distance, rejecting interior points afterward: O(radius³) iteration for O(radius²) target positions. It runs even after all expected chunks exist. Generation rebuilds a loaded-chunk map before finding there is no work and can generate two chunks/frame. `ChunkProjectionSystem.cs:19` budgets one projection/frame but each active projection traverses the voxel volume with large temporary occupancy maps.

First low-risk candidates: directly enumerate ring perimeters, gate fully satisfied unchanged streaming center/settings, no-work generation early-out. Then measure generation/projection spikes and consider scheduled jobs. Keep chunk creation/destruction budgets, neighbour invalidation and streaming backlog semantics intact.

### 7. Secondary recurring passes and periodic spikes

- WorldItemLightSystem visits disabled/non-light items and writes positions/light data every frame; WorldItemDurabilitySystem visits/writes items without decay. Both do linear item-definition lookup. Split relevant populations or bake lookup/cache components instead of recomputing inert properties.
- IgnitableCellSystem clears/rebuilds from all items each frame and deduplicates with a linear buffer search (quadratic in distinct ignition sources). Important at large torch counts, less likely the main culprit with mostly Flint/Pyrite.
- CoastalItemSpawnSystem snapshots/scans all loaded chunks every frame even when all are processed. Use new-chunk work queues/change gates while preserving spawn-once history.
- CoastalAudioSystem.Sample runs at 4 Hz, completes dependencies and scans nearby ocean surface samples/builds shore masks. Possible periodic spikes, not an explanation for every slow moving frame without a trace.
- HUD minimap already has revision gates and tile updates; avoid replacing it wholesale. Smaller issues include duplicate durability setter calls and repeated tooltip/message work. Lower priority than item rendering/projection.
- Input-triggered skill graph collections and world-item searches do not execute continuously when no skill key is pressed; not a strong idle/movement suspect.

## Measurement and implementation order

### First item-render patch implemented

WorldItemRenderSystem now tests full projected sprite bounds against active camera frustums before ground-clipping tests, mask generation/uploads, lighting lookup and draw submission. Reuses camera/plane arrays rather than allocating them per frame. Includes Editor Scene views to preserve their rendering, multiple cameras and render targets; no-camera state fails open. Objects intersecting a viewport edge are retained. Definition lookup/projection and the item iteration remain; no spatial item index or instancing was added.

Replaced per-frame lightingTiles clearing and per-item linear projected-buffer scans with a lazy per-loaded-chunk dictionary indexed by local X/Z and half-height. Each dirty visible chunk is scanned once; unchanged frames and missing tile queries use dictionary lookups. First-match semantics, Top-only/cut-top exclusion and half heights match the old scan. ProjectedCellData change versions invalidate both geometry and light data; chunk entity replacement invalidates even at unchanged coordinates. Dirty state persists while offscreen. Unloaded chunks and stopped systems clear index entries. Current SunShadowState still applies every draw, so changing sunlight does not depend on a stale cached ambient value.

Profiler markers: WorldItems.Render and WorldItems.BuildLightIndex. Physics, item population, persistence, shader math, per-item submission format and global occlusion invalidation are unchanged. Offscreen masks may be released by the existing EndFrame cleanup and rebuilt when re-entering view.

Verification: runtime C# compilation passes (existing unrelated FindFirstObjectByType obsolete warning remains). WorldItemRenderChecks executes the extracted production index builder against the previous first-match search over full/half/cut/side/duplicate/missing/rebuilt cases, using buffer adapters and real Unity.Mathematics. Camera control flow is tested with a frustum adapter for no-camera, offscreen, overlapping edge and second-camera cases; Unity native camera/frustum execution, actual visual correctness and FPS have not been measured. Test in the same location/zoom/resolution/power mode, standing still and moving with a torch, and rotate view/check cave occlusion before accepting the patch.

1. Capture CPU Timeline/Hierarchy + GC.Alloc, GPU time and Rendering counters in the same location for 15–30 s static, movement inside loaded terrain, then movement into new chunks. Keep Deep Profile off for representative timings. Also reproduce in a Development Build; Editor overhead is not player performance.
2. Repeat moving segment without lit torch; then diagnostic A/B disabling only item presentation (not removing/saving/deleting items). Such toggles are proposed, not applied by this audit.
3. Record item totals/awake/visible/draw submissions, occlusion rebuilds/masks uploaded, active chunks, pending projections and local-light affected chunks alongside the existing ChunkRender.FullSnapshot / ChunkRender.LightRanges markers.
4. First patch: item visibility rejection + cached top-surface lighting lookup; verify sprites, shadows, clipping, view rotation, half-blocks and loaded-area edges.
5. Next: localize occlusion invalidation and remove idle streaming/spawn work; measure each independently.
6. Larger follow-up: stream item residency/persistence, incremental render uploads, scheduled generation/lighting. Do not combine them into one unmeasurable rewrite.

No numerical FPS improvement is promised by source inspection. The code establishes multiple avoidable costs; profiler A/B tests establish which dominates this scene.

## Isolated SkyLight traversal patch

SkyLight invalidation originates in chunk creation and block modification, not player movement itself. Keep the existing immediate/non-immediate scheduling and one background chunk per frame unchanged. Keep terrain projection unchanged after the reported regression and rollback.

SeedVerticalLight now stops at the first full/upper-half opaque block, or immediately after lighting a lower-half block. The light buffer is still cleared first and lateral propagation remains responsible for cells below overhangs. TrySpread rejects targets with sufficient skylight before reading/resolving source and target geometry. No lighting rules, split geometry resolution, queue contents, chunk invalidation or RGB values are intentionally changed.

SkyLightChecks.ps1 compiles the production calculation bodies and reconstructed previous control flow with managed buffer/queue and resolved-block adapters. Nine 16x256x16 fixtures cover empty/solid chunks, terrain, caves, shaft, upper/lower halves and water. Every voxel's skylight matches; RGB is preserved. Counted block reads fall from 7,303,760 to 585,996 in these fixtures. Actual packed split decoding, native containers, Burst performance and Unity FPS are not measured by this harness. Compare SkyLightSystem timings while entering new chunks and editing terrain in Unity before accepting the performance result; also visually check caves and half-block overhangs.
