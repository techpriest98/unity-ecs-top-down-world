# Character normal painting and isolated lighting preview

Open **Tools > Character Animation**. The right panel has **Normal painting / lighting**:

- **Preview / Paint** tabs separate the tools. Preview always shows lit diffuse and lighting controls. Paint shows raw encoded normal colors without lighting and only the paint palette/layer/save controls; playback pauses and emitter controls/markers are hidden. Lighting settings and unsaved drafts survive switching tabs.
- **Camera** rotates the world sun into sprite space; the existing Down/Up/Left/Right buttons select the sprite-facing row. Camera is not another independently authored hand anchor set.
- **Sun** enables direct sun. **Hour** uses the game's DirectionalLightUtility.Sample (integer 0..23, as in runtime). Ambient follows that time, with an independent **Ambient** multiplier (zero isolates direct lights).
- **Item lights** uses equipped items' Light Definitions and current per-frame anchors. Radius/intensity use full durability; radius clamp and max-per-channel accumulation match runtime. Hidden/empty item frames emit no light in the editor preview. Custom profile Z inversion is shared with runtime through RuntimeAnchor; automatic sources use SpriteOffset. No game global shader state is changed.
- **Feet light** adds one screen-relative test source. Position X=right, Y=up from feet, Z=toward camera (world units), with radius/color/intensity controls and a colored square marker. Default height .05 is below most body pixels; try Y=-.2 and Z=0 to test underside response.
- **Height** sets the upright-sprite world scale (default 2). Feet baseline pixel Y=25, center X=32, height spans 98 pixels, matching current runtime normal-lighting approximation.

Lighting is an editor-only CPU preview of isolated sprite planes: no voxel walls, sun shadows, world sky occlusion, voxel residual fill, durability decay, tone mapping or fog. It is not an exact in-game scene render. Colors are converted for the project's color space; alpha-cutout pixels are discarded. Maps must be readable and match diffuse dimensions; failures show a warning and fall back to unlit drawing. No automatic importer changes are made.

## Painting

Open **Paint**, choose **Paint layer**, select direction/frame and a swatch, and draw with the left mouse button. Palette formula is identical to the current block editor's 4x3 direction picker; brush RGB is shown with tooltips. Brush is one source pixel, interpolates strokes, and locks both existing normal and diffuse alpha. It cannot create coverage outside the existing normal map. Return to Preview to adjust emitters and inspect lighting. Zoom supports 1..16.

Normal edits use separate serialized drafts, so Unity Undo/Redo operates before saving without mutating imported assets. **Save maps** writes dirty PNGs only, creating timestamped original-file backups in Assets/Docs/Backups/CharacterNormalPainting. It refuses non-project/non-PNG files and refuses to overwrite a texture changed externally since the draft was loaded. Saving is an Undo boundary. **Discard** clears unsaved drafts after confirmation. EditorWindow's unsaved-changes prompt also invokes save/discard on close. Clean drafts reload on external imports. Diffuse textures and animation metadata are never painted.

After saving, rebuild the animation atlas to update the game. Painting itself updates the editor preview immediately; it does not rebuild atlases or mutate game lighting.

## Verification

CharacterNormalPreviewChecks.ps1 compiles actual preview utility code with headless Unity math/color shims and tests all twelve swatches against the current block-picker expression, frame/direction/Y-flipped brush indices and point lights below/coincident/outside radius. PlayerNormalTestChecks.ps1 tests shared profile Z conversion and runtime light math/HLSL. Runtime and Editor assemblies compile. Actual Unity IMGUI dragging, Undo/close prompts, importer refresh, color-space appearance and Play Mode remain visual integration checks.
