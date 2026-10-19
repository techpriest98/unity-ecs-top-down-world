# Height colour grading restored

ChunkProceduralRenderer -> Terrain Height Readability (GPU): Strength defaults to 0.3 (0 disables grading), Tint is the original cool 0.62/0.73/0.85, Range starts 0.5 blocks above player feet and reaches full strength at +6 blocks, Desaturation defaults to 0.65. Only opaque normal/clipped terrain is graded; water, UI and fog colour remain unchanged.

SourceAirIndex is already packed in high GPU FaceData bits for terrain fog masking. The shader reuses it to recover face height (current chunks 16x256x16) without changing buffers or GPU stride. The existing render system supplies player height before early-outs. No new terrain traversal, texture read or draw pass is introduced. Sky visibility suppresses grading in enclosed areas, cut caps keep their black early return, and the already-lit colour is graded without adding emission.

Current world fog, wind/F4 controls and always-active player clearance centred one block above the player are preserved. WorldFogPlaneChecks compiles both fog shaders and the opaque terrain HLSL, including clipping/debug variants. Compare Strength 0/0.3 and inspect the same hill across four views; actual Unity visual/performance validation remains manual.
