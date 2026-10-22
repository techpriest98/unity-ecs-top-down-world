# Experimental player silhouette sun shadow

Local-light update: the same silhouette now also supports point lights, independently of sun/night. See PlayerLocalSilhouetteShadow.md.

Enabled by default on ChunkProceduralRenderer: **Player Sun Shadow (experimental)** / **Player Sun Shadow Strength** (default 0.8). Disable the checkbox or set strength to zero to compare. No player animation atlas rebuild is required. Let Unity import the new Resources material/shader and re-enter Play Mode.

PlayerSunShadowRenderSystem obtains the local player's active visual parts, diffuse Texture2DArray, atlas UV/page and trimmed source-cell geometry. A 64x128 linear RenderTexture receives one procedural draw (up to 8 instances, currently body/arms/items) using binary alpha and max blending. Overlapping parts therefore do not double-darken the shadow. No CPU texture readback, chunk rebuild or per-receiver draw is used. The mask material is loaded from Resources so its shader reference is retained in builds.

ChunkProcedural.shader projects visible Top receiver pixels back along the sun into the upright source silhouette. It uses the existing projection position and packed source-air height; no new block GPU record fields or stride changes. Projection inversion uses the same world-to-screen helper basis, the +0.25 projected tile-center offset, source center X=32 / feet Y=25, and player Height/98 scale. Shadow only reduces the direct sun term, leaving ambient and point lights unchanged. The existing terrain depth/occlusion and actual visible Top geometry decide where it appears, so no independent shadow quad floats over empty space or paints over side faces.

## Pilot limits

- One local player and one shared diffuse atlas; dynamically registered material-only parts without a RenderMeshArray are skipped.
- Receiver height must be within 0.55 world units of player feet; lower floors, high ledges, walls, water and projection cut caps do not receive the shadow. It can disappear while jumping.
- Night/occluded sunlight disables it via PlayerNormalLighting.Sun. Existing sun occlusion uses its current player sample, not a full caster visibility volume.
- This is an upright **billboard silhouette**, not a 3D body. Minimum toward/up slope magnitude 0.15 gives an artistic thickness at edge-on angles. Slopes clamp to +/-4 and receivers to radius 8 world units to keep dawn/dusk projections bounded. Close to edge-on angles the shape/direction can change noticeably.
- Hard pixel shadow, no soft penumbra, no torch shadows or self-shadowing. Current body lighting Height/98 approximation is reused.
- No GPU/FPS claim: there is one additional small mask draw plus nearby Top shader sampling. Profile actual scenes before keeping it enabled permanently.

Global shadow availability is reset on stop/destroy and each update before validation; resources are released on system destruction. Source artwork, existing sprite sorting and item light anchors are unchanged.

## Checks

PlayerSunShadowChecks.ps1 compiles the actual mask HLSL for both render-texture UV conventions and exercises actual C# projection helpers across all four views, half-height receiver offsets, zero/edge-on sun and negative chunk boundaries. WorldFogPlaneChecks.ps1 compiles the changed terrain shader including clipping/debug variants and checks projection invariants. Runtime assembly compilation succeeds. Actual Unity shader import, material resolution, RenderTexture orientation, ground contact and visual appearance still need Play Mode validation.
