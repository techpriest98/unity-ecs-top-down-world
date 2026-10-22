# Sun lighting A/B test

WorldScene / ChunkRenderer / SunLightingDebug has two independent checkboxes:

- CPU Sun Shadows (default on): existing voxel sun-ray calculation for terrain/water chunks.
- GPU Sun Lighting (default off): bounded directional sun visibility near the player, using the local-light geometry cache, cached visibility plus exact boundary rays, and the same alpha silhouette/anchor for player shadows.

CPU-off is real work suppression: SunShadowState follows sampled light directly without queued mask transitions; ChunkLightingSystem cancels shadow-only work and short-circuits sun visibility when calculating full cell lighting. It still performs required skylight/local-light corner work. Terrain/water ignore old sun mask bits while disabled. Re-enabling queues all generated chunks for fresh target masks through the existing budgeted transition. World generation/skylight are not disabled. PlayerLightingSystem's small per-player CPU sun test is retained; this toggle targets CHUNK sun work, not every CPU sun operation.

The radius-sixteen experiment has been reverted. GPU sun and local lights cover radius eight again: full radius/relative height seven, blend seven to eight. Sun cache is 20x40x20 uints (62.5 KiB), shared geometry is back to 40x80x40 (500 KiB). Actual geometry revisions/snapped movement or ACTIVE sun direction changes invalidate the sun cache. Trace indexing still reads the grid size, but no expanded geometry allocation remains. Sun rays retain their previous sixteen-world-unit maximum; receiver radius and ray distance are different limits. Unknown coverage falls back to CPU shadows if enabled, otherwise unoccluded sun. CPU-off/GPU-on is not world-wide shadowing. Cut surfaces/water retain base behaviour; GPU mode handles terrain tops/sides. Skylight and local lights remain independent.

The player shadow uses parallel rays into the same silhouette as point lights, with the existing shared shadow offset/strength. No new artistic offset is introduced. GPU mode uses its own directional visibility, not the CPU shadow bit inside the known region. Existing normal response still handles the sunlight direction. Sun rays are not simulated with a distant point source.

Directional silhouette correction: intersect the infinite directional ray directly with the caster plane. Do not call the point-light segment helper with a fabricated source 1000 units away: its normalized 0.001 segment epsilon discards approximately one world unit near the caster and detaches/truncates the legs' shadow. The directional helper uses a small world-space distance threshold instead. Shared mask/anchor/strength and the bounded receiver region remain unchanged.

Suggested comparisons with the same seed, view, position and time (change/save checkboxes BEFORE starting Play Mode):

1. CPU on, GPU off: baseline.
2. CPU off, GPU off: isolate CPU chunk-shadow cost; unoccluded sun expected.
3. CPU off, GPU on: proposed near-player GPU replacement.
4. CPU on, GPU on: near-player GPU refinement with distant CPU fallback.

Measure first visible world, time until pending projection/lighting finishes, and FPS during/after loading. Repeat runs to separate Unity/Burst/shader warm-up and OS cache effects. Remaining one-chunk-per-frame lighting budgets and generation still affect load time; removing sun work does not remove these limits. No speedup is claimed before measurement. GpuSun.VisibilityDispatch is a CPU compute-submission marker, not GPU duration.

Safety: no control component means original CPU mode. Missing compute support/resource leaves GPU off. Shader globals reset when systems/components stop. Cache resources release on world destruction. WorldScene starts with baseline controls. Runtime, compute/terrain HLSL and arithmetic/source contracts are headlessly checked; visual agreement, startup loading and real GPU cost need Play Mode verification.
