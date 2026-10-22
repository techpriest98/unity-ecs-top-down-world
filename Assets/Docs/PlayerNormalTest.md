# Body Idle normal-lighting pilot

The existing Player material now uses `Game/Player/NormalTest`. Only Body/Idle with two frames reads `body_idle_diffuse_clean.png` and `body_idle_normal.png` directly. Body walking, arms and equipment still read the existing Texture2DArray and `_PlayerLightColor`, with no atlas regeneration or new draw pass. The original Shader Graph and diffuse sheets are preserved.

Restart Play Mode after Unity finishes importing/rebaking. Stand still to use the pilot; walking deliberately returns to the old body artwork. Inspect from all four camera views and character facings, in sunlight, cliff shadow and beside a lit torch. Disable fog with existing F4 if it obscures the comparison. Sun direction currently changes once per game hour; the existing F5 time accelerator can speed the test.

## Material controls (Art/Player/Player.mat)

Rendering uses AlphaTest/TransparentCutout, Blend Off, ZWrite On and ZTest LEqual. Both the normal-body and legacy atlas paths discard alpha below **Alpha Cutoff** (default 0.5). Thus transparent quad corners do not write depth, and the existing per-part ZIndex/tie-break offsets control actual overlap irrespective of draw order. This restores the previous opaque/cutout shader's layering behaviour, replacing the pilot's incorrect transparent/no-depth-write state. Lighting/normal calculations are unchanged by this state fix.

- **Enable Body Idle Test**: 1 = cleaned diffuse plus normal shading; 0 = original animation-atlas rendering, including original baked albedo shading.
- **Normal Strength**: 1 = authored normals, 0 = flat forward-facing normal using the cleaned diffuse. Use this for a normal-only comparison.
- **Show Encoded Normals**: unlit visualisation of decoded/blended directions on the tested idle body; other parts are unchanged.
- **Painted Detail In Shade**: original `body_idle.png` provides albedo detail for ambient/residual fill only (default 1; 0 restores clean diffuse). Direct sun/point terms always light the clean diffuse.
- **Direct Light Detail Fade**: smooth per-pixel direct-light luminance threshold at which painted ambient detail disappears (default .35). There is no emissive or minimum-light floor: no light remains black. The current alpha-tested silhouette still comes from the chosen test diffuse; original paint does not add opaque pixels. This adds one texture sample, not a render pass. In shade the original left-to-right painted form is intentionally retained, not physically reconstructed ambient lighting.

## Lighting and scope

`PlayerLightingSystem` keeps its existing legacy colour and writes a separate local-player lighting record: sky-scaled ambient, shadow-tested sun and a buffer of up to eight nearest enabled, nearby, torso-visible dynamic lights. A visibility ray per candidate uses existing block/half-block geometry. The presentation bridge projects source positions relative to the player feet into sprite-right/world-up/towards-camera coordinates. This matches the block editor's RGB normal palette, not a conventional Unity tangent-space normal texture. Normal pixels missing alpha use a flat normal. Black albedo remains black.

The shader evaluates distance, attenuation and normal dot light direction separately at every pixel of an approximate upright sprite plane. Plane dimensions use collision height and the idle sheet's 98-pixel body span/25-pixel foot baseline; this is not an actual reconstructed 3D body. At exact source/surface coincidence the directional term is zero, never full brightness. Local sources combine by per-channel maximum, matching the voxel lighting design rather than adding eight lights to white. Selected sources' estimated voxel contribution is removed from the voxel sample; only 15% of the remaining positive residual is retained as fill. This is conservative, approximate separation because voxel RGB stores maximums, not per-source sums. The old zero-gradient/full-isotropic fallback is removed.

Occlusion is tested at the torso, not per fragment: some partially occluded sources may disappear for the whole body, and ground/upper-body visibility can differ. Beyond the eight selected sources, indirect/unrepresented voxel light appears only as the small residual. A single local player supplies global lighting parameters; multiplayer/multiple simultaneous player lighting contexts are outside this pilot. World, arms and equipment legacy lighting are unchanged.

Held torch/staff emitters use `PlayerHeldLightPositionUtility`: .65 world units to the hand side, .35 forward and .75 times collision height above the feet. The authored front-facing sprites place the left-hand item on screen-right (+X when facing +Z); the right-hand source is mirrored. Offsets rotate with `PlayerFacing`, never with the camera. These are approximate shared offsets, not animated per-item socket tracks. Ground item light positions and the player's separate debug source are unchanged. Existing world DynamicLightSystem cell-change detection is unchanged; body normal-light direction now uses actual sub-cell source positions even when source/player share a voxel.

Frame UVs are reconstructed from the existing trimmed frame Size/Offset, so diffuse and normals use exactly the same source-sheet rectangle despite the animation's packed atlas layout. No per-pixel CPU work or extra draw pass is added. CPU work scans enabled sources and visibility-tests nearby candidates; GPU work is bounded to eight point lights on the idle body. Actual cost needs profiling. Normal map import is linear, point sampled, without compression or alpha colour dilation; its painted PNG and the user's current diffuse binding are unchanged by this lighting fix.

## Verification

The player pass uses **SRPDefaultUnlit**: the project's active Renderer2D accepts it (and Universal2D), but does not draw UniversalForward passes. The initial pilot incorrectly used UniversalForward, making all player parts disappear. The regression check now verifies the material shader's pass tag against the installed Renderer2D source and the pipeline asset's renderer GUID.

Runtime source compiles with the project's Unity response file. `PlayerNormalTestChecks.ps1` checks actual trimmed UV/basis utility code for both frames and four directions, and compiles the shader's vertex/fragment HLSL with transform/instancing adapters for its regular and DOTS code paths. The adapters do **not** validate actual Unity DOTS integration, SRP state, images or performance. Those require Unity import and Play Mode.

## Rollback

Disable **Enable Body Idle Test** for the original atlas appearance. To restore the previous shader completely, select `Art/Player/PlayerSprite.shadergraph` on the Player material (original shader GUID `a992cc28824dd4beb9d1f08d6619a18d`); the existing `_SpriteArray`, `_SpriteIndex`, `_UvScaleOffset` and `_PlayerLightColor` bindings have been retained. The new components/lighting bridge can remain while the test is disabled.
