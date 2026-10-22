# Animation diffuse / normal atlases

Update: optional AnimationDefinition.PaintedMap and a matching PaintedTextureArray now support original painted detail in weak direct lighting. See PlayerPaintedShadeMap.md; the paragraphs below describe the original diffuse/normal pipeline.

`AnimationDefinition.NormalMap` is optional and shares the SpriteSheet layout: 64x128 cells, FrameCount columns, four direction rows. SpriteSheet is the diffuse used at runtime; no separate test or painted-detail texture remains in the shader.

Normal inputs are raw RGB in the block editor basis (right, world-up, toward camera), not Unity packed normal textures. Import: sRGB off, Read/Write on, Point, mipmaps off, uncompressed, Alpha Is Transparency off. Dimensions must match diffuse. The builder validates these settings without changing source art. Normal alpha marks availability; diffuse alpha alone controls the silhouette.

Select PlayerAnimationsDatabase and run **Assets > Game > Build Animation Texture Array**. The builder creates a new AnimationAtlas containing both TextureArray (sRGB) and NormalTextureArray (linear). Normals use exactly the same diffuse trim, sort, placement, page and extruded padding. Missing maps use alpha-zero flat normals, selecting legacy lighting for those pixels. No independent normal trim or packing is performed.

TargetMaterial on the database receives both arrays after building. Assign the newly created atlas to PlayerAnimationLibraryAuthoring on the player, as with the existing build workflow. Existing atlas assets are not overwritten. The baker rejects old atlases lacking a matching normal array.

BodyIdle uses body_idle_normals; BodyWalk uses sprite_walk_palette. SpriteSheet references remain as authored: assign clean diffuse sheets there as desired, and Trim All if silhouette bounds change. The builder does not flatten artwork automatically.

The shader retains alpha-tested depth writes and the established point-light response. Frame geometry now reconstructs the upright surface from trimmed size/offset rather than a hardcoded two-column Idle sheet. No extra draw pass is added. Painted ambient detail is removed; pure ambient light alone will not reveal normal-map shape. Local-player lighting globals and the approximate upright surface/torso visibility limitations remain.

Checks: AnimationNormalAtlasChecks.ps1 exercises actual production copy methods; PlayerNormalTestChecks.ps1 compiles shader variants with headless adapters and checks point-light math. Unity import and Play Mode visuals still require validation in the editor.
