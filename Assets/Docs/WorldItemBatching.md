# World item draw batching

WorldItemRenderSystem now accumulates consecutive visible sprites sharing the same underlying texture into batches of up to 128 instances. Each page submits one Graphics.RenderPrimitives call instead of one call per item. Global texture sorting is intentionally avoided: equal-depth alpha-tested overlapping items retain their original submission order. Thus actual calls equal consecutive texture runs split at 128, not necessarily unique item types/textures. An alternating texture sequence can still require one draw per sprite. Source sprites/import settings and item assets were not modified.

WorldItemDrawData is eleven sequential Vector4 fields (176 bytes), mirrored by the HLSL StructuredBuffer record. Per-instance data retains sprite rect/UV/depth, local-light tile and corners, ambient/sunlight, and grounded/occlusion flags. The vertex shader uses SV_InstanceID; no Unity material instancing keyword or sprite atlas asset is required. Existing early frustum rejection and revision-based ground-light lookup remain in place.

Individual CPU occlusion masks are copied into a per-page R8 atlas; the renderer requests masks without per-mask GPU Apply. Mask content versions change whenever pixels are regenerated. Slot copies compare both source texture and version, so reordered entities, moved grounded sprites, projection changes and newly created masks refresh correctly. Atlas dimensions grow with used slot counts and the largest mask dimensions; growth invalidates all copied slots. Only dirty atlases Apply. Shader integer Load uses each mask's actual pixel size and clamps the edge to prevent bleeding into adjacent slots. Airborne items disable mask reads.

Pages own their structured GraphicsBuffer, MaterialPropertyBlock and atlas. Extra pages dispose when visible batch count shrinks; all pages dispose on system stop/destruction. Small runs do not allocate a full 128-slot atlas, though instance arrays/buffers retain page capacity. Geometry buffers still upload current instance records once per used page/frame. Global CPU occlusion invalidation and per-entity CPU mask caching have not been redesigned in this patch.

Diagnostics: WorldItems.Render and WorldItems.SubmitBatches profiler markers, plus managed-system LastVisibleItemCount/LastDrawCallCount properties. Unity Frame Debugger can inspect instance count per item batch. Compare identical scene/camera/zoom, power mode and static/moving paths before judging FPS benefit; reduction is workload/texture-order dependent.

Verification: runtime C# compilation and actual HLSL vertex/fragment D3DCompile checks pass (Core transform/sample macros adapted for the standalone shader test). WorldItemBatchChecks executes production batching against graphics/storage adapters: ABI stride, texture/capacity boundaries, counts, atlas placement, unchanged-upload suppression, version updates, reordered slots, dimension growth and disposal. Item-render index/culling and item-streaming regressions pass. These are not Unity GPU/URP visual or measured FPS tests.

Manual acceptance: inspect Flint/Pyrite/shards/torches at camera edges, ground/air transitions, stacked different items, caves/cliffs, view rotation, torch lighting, daylight changes and chunk unload/reload. Confirm no pink shaders, mask seams or incorrect light sharing. Check Frame Debugger and compare batches with visible item counts; do not infer that draw submission was the dominant bottleneck merely because batching is implemented.


## Local occlusion attempt rolled back

The chunk-snapshot/projected-stripe occlusion change was reverted after the user reported FPS falling to approximately 10. The previous global hash/rebuild and flat reusable face chains are restored. Headless correctness/allocation checks did not establish Unity performance; the exact cause of this regression has not been isolated. SkyLight and earlier item-render optimizations are unchanged.
