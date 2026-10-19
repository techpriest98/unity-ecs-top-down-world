# Open-ocean waves — pixel foam patches

## Current revision: authored masks

### Sky visibility

Both legacy/full and split-cell water Top projection paths set an explicit sky-visible flag only when the cached column surface is fluid at that exact half-height, matching the initial visibility criterion used by shoreline foam. Covered/cave water skips GetOceanWaves entirely (foam and wave normals); ordinary water shading remains. A zero shore-contact mask alone is not sufficient because open sea also has no shoreline contacts.

Water-only BlockData bit 16 carries this flag in the otherwise unused water durability channel. It is explicitly cleared/set by WaterProjectionWriter, does not affect opaque durability, and does not change render-buffer stride. Existing sky-surface cache/projection invalidation provides updates after terrain changes. C# compilation, four HLSL compile checks and ocean math regressions pass; cave/open-sea appearance still needs Play-mode confirmation. Restart Play mode to rebuild existing projection records.

### Connected-ridge experiment

The leading crest is now a continuous narrow procedural ridge on each bent wave band, with smoothly varying width/intensity. Atlas masks supply porous trailing detail only; transparent cell borders no longer cut the leading ridge. Spawn selection is per wave band rather than per segment. Lifetime phase varies continuously along the band, so breaking progresses instead of switching entire segments. Normal bulges likewise have no segment-end fade. Material dimensions and shoreline behavior are preserved; Patch Length now sets trailing-pattern spacing rather than disconnected crest length.

HLSL compilation and math regressions pass, including continuity at old segment boundaries, ridge support, lifecycle and all camera views. Current sparse fixture: 185/16384 samples. Actual game appearance/performance remain to be evaluated.

### Breaking-wave lifecycle

Patch age now drives a subtle local normal bulge before foam appears. Leading foam develops at age .18–.30 and collapses during .42–.64; the trailing body develops during .30–.48 and persists until .98. Fixed logical-pixel hash thresholds progressively erode late foam without animated noise or adding pixels outside the atlas mask. Existing patch spawn/lifetime, integrated wind advection, material dimensions and shoreline foam remain unchanged. This is a shading approximation, not fluid simulation or independent particle drift.

Verification after this change: four HLSL compile checks passed, along with projection/wind/shore math regressions and explicit checks for pre-break absence, leading-first formation, lingering tail, cycle-end disappearance and preservation of transparent artwork. Sparse fixture now measures 128/16384 samples. Final visual tuning and GPU timing still require Unity.

Four authored masks now replace the procedural thin strokes described in the historical notes below. Each has an irregular leading crest, porous body and scattered trailing crumbs. Stable hashed variants follow bent wave bands, with independent slow lifetimes. Existing world wind drift, pause-aware clocks, wave lighting and shoreline suppression remain unchanged. Shoreline artwork is untouched.

`Resources/OceanFoamAtlas.png` is a 64×32 atlas of four 32×16 cells: white RGB, alpha levels 0/80/160/255, 431 nonzero texels out of 2048, transparent cell borders. Import is linear, point-filtered, uncompressed, without mipmaps. Explicit texel loads prevent cell bleeding. The renderer caches the Resources texture; missing texture disables ocean foam. No new draw calls, geometry uploads or render-record changes are introduced. One texture load is added per active patch fragment; actual GPU timing remains unmeasured.

New material controls: `Ocean Foam Patch Length (Blocks)` = 2.8 and `Ocean Foam Patch Depth (Blocks)` = 1. Existing settings are preserved.

Source study: `Docs/References/OceanFoam-Mask-Study.png`, generated with the built-in image generator using shoreline art as style reference only. `Tools/BlockAtlasEditor/Editor/ImportOceanFoamMasks.ps1` deterministically packs/quantizes the study and refuses to overwrite an existing atlas. Exact prompt: `Docs/OceanFoam-Mask-Prompt.md`.

Current verification: clipped/unclipped vertex and fragment HLSL compile checks passed; runtime C# compilation and wind regressions passed. The math harness checks every atlas texel against the PNG, orientation, transparent borders, stable variants, valid placements, four camera views, production projection, negative chunks, drift invariance, frozen clocks, zero strength, shore suppression and finite normals. Current sparse coverage fixture: 284/16384 samples. Retired procedural shape/segment helpers and their tests were removed. URP uses minimal compile adapters and CPU derivatives are zero; these checks do not constitute a Unity GPU image or performance test. Diagnostic preview was inspected; final game appearance still needs checking.

## Historical implementation notes (superseded foam shapes and counts)

`Shaders/OceanWaves.hlsl` adds shader-only movement to OceanWater (BlockId 4) top faces. River water and vertical faces keep the previous behavior. No mesh deformation, new draw calls, per-cell animation state, terrain/projection rebuilds, or GPU buffer layout changes are introduced. CPU work adds a projected sea-plane origin uniform; wave shape and crests are fragment-shader work.

## Material settings

Both `Art/World/ChunkWaterProcedural.mat` and `ChunkWaterProceduralClipped.mat` use:

| Inspector parameter | Initial value | Purpose |
| --- | ---: | --- |
| Ocean Wave Strength | 0.75 | Overall effect; 0 restores previous shading/foam behavior |
| Ocean Wave Length (Blocks) | 3.4 | Size of broad wave families |
| Ocean Wave Speed | 1 | Integrated drift and crest life speed |
| Ocean Wave Normal Strength | 0.22 | Lighting slopes; does not move geometry |
| Ocean Crest Foam Strength | 0.6 | Brightness/opacity of broken crests |
| Ocean Crest Foam Coverage | 0.65 | Frequency of patches that can develop foam |

## Behavior

- Inverse projection reconstructs sea-plane world XZ from projected positions using the current view axes and sea surface height (`WaterSurfaceHeight`, not the exclusive water fill bound). The +0.25 projected-Y offset accounts for the difference between entity-center projection and actual rendered Top geometry.
- Fixed wave families plus fine ripples are advected by accumulated world wind drift. Wind changes their lighting weights smoothly rather than rotating a position-times-time phase across the world. Only one family generates foam; the weaker crossing family and fine ripples affect lighting only.
- World-space logical grid, continuous noise, and shared clocks avoid per-tile repeats. Crest edges and fragment gaps are pixel-sharp. Fragment derivatives fade unresolved crests at distance rather than broadening them into fuzzy strokes.
- Sparse crests appear/disappear through noise modulation. Their tinted color is combined before ambient/local/sun lighting, not emitted as unlit white. Lighting slopes currently affect directional sunlight; water still uses its existing interpolated local light rather than a new local-light normal calculation.
- Cardinal and diagonal shore masks fade open-ocean foam near land and rock corners. Existing shore foam is composited last and further suppresses ocean crests. Shore textures/parameters remain unchanged.
- Existing pause-aware foam and wind clocks are reused; no `_Time` scrolling is added. Material speed zero freezes the new pattern's own motion.

## Verification

`Tools/ItemGraphEditor/Editor/OceanWaveChecks.ps1` compiles the expanded actual water HLSL with Windows D3DCompile for vertex/pixel Shader Model 5, with/without player clipping. Local includes are actual production code; the URP Core texture/transform declarations are minimal adapters, so this is not an assertion of full Unity pipeline compilation or a GPU image test.

The math harness translates actual OceanWaves/shore-noise expressions to Unity.Mathematics and checks all four views against the production world projection and independently reconstructed renderer Top-quad geometry, including negative chunk boundaries. It also checks integrated drift invariance, frozen clocks, zero-strength identity, cardinal/diagonal shore suppression, finite unit normals, hard binary resolved crest edges and sparse visible crests (207/16384 fixture samples after the segmented-arc revision). Variable widths and the proximity/sparsity/lower brightness of chips are checked explicitly. Segment-mask runs are checked across nine positive/negative wave bands: full runs exceed 1.2 blocks, no run exceeds 4.1 blocks, and over 100 full segments are exercised. Actual visible fragments can be shorter where the existing breakup mask intersects a segment. CPU derivative adapter is zero; real derivative syntax is covered by HLSL compilation, not by visual aliasing tests.

Runtime C# compilation and existing wind regressions passed. The 48-byte render record is unchanged. Real FPS, fill cost, final in-game appearance, distant shimmer and Unity's actual shader variants must still be assessed in the running editor/build, especially at zoom 20.

## Pixel-crest revision

The first in-game result showed soft intersecting dashes/check marks. The revision removes secondary-family foam, reduces secondary lighting slopes, bends a single foam family into longer arcs, breaks it with hard logical-pixel gaps, and reduces the crest color mix from 0.72 to 0.42. Spatial edges are crisp; a separate slow envelope retains smooth changes in time. Existing material values, wind linkage and shore suppression are preserved.

`OceanWaveChecks.ps1 -Preview` optionally renders a temporary diagnostic foam-mask image using the actual shader expressions, with the projected surface's 2:1 pixel aspect. This was visually inspected during revision; it is not a Unity screenshot or a GPU timing test.

The follow-up keeps that shape and direction but varies crest half-width from 0.032 to 0.125 in phase-mask units using smoothly varying along-crest noise. Sparse adjacent chips are only allowed near thicker parts and within a bounded crest neighbourhood; they have 35% of the ridge intensity and cannot form a continuous halo or separate parallel ribbon. The same crisp spatial edges, clocks, wind advection and shoreline fade remain in use. Both Direct3D shader variants and the math checks passed; in-game appearance still needs evaluation.

The next revision breaks the long arcs with a dedicated along-crest segment mask. Each wave band has its own offset and every segment has unequal, hashed endpoints. A small monotonic domain warp varies spacing; visible spans are typically 2–3 blocks before the pre-existing local breakup is applied. Both the ridge and its adjacent foam chips are removed in the gaps, preventing a faint continuous line from bridging them. Shape, light intensity, material settings, movement and shore suppression are unchanged. Diagnostic mask inspection and both clipped/unclipped HLSL compilation passed; final Unity appearance remains to be checked.
