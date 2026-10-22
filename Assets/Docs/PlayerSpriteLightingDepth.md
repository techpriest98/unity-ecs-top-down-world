# Held-item lighting depth

Player item draw order remains the existing discrete frame ZIndex and tiny transform Z offsets. These are not physical lighting depth and have not been changed.

PlayerEquipmentLight now identifies its current visual part. PlayerNormalTestRenderSystem uses that emitter's final DynamicLightWorldPosition (including near-wall correction) to derive the corresponding visible item's physical depth in the current camera basis. PlayerSpriteLightingDepth is baked as an instanced material property; PlayerNormalTest.shader uses it in each item's lighting-surface coordinates. All part depths reset to zero before matching enabled held emitters, so unequipping/changing an item cannot leave stale depth. Body and non-emitting parts retain their previous zero-plane behaviour.

Both runtime and character preview use PlayerNormalTestUtility.EmitterSurfaceDepth: the illuminated face is one source pixel behind its emitter (height/98 world units, minimum .005). This prevents exactly coincident/coplanar self-lighting while deriving the surface from a single existing anchor rather than another manually tuned Z setting. Preview does not have world-wall correction, as before.

No texture, atlas rebuild, normal asset or saved anchor changes. Restart Play Mode after rebaking visual entities for the new material component. This is NOT emission: flame pixels still use normals/attenuation and eventually go dark with insufficient radius/intensity; no promise of a permanently glowing flame without an emissive mask. Legacy no-normal lighting retains its existing per-player fallback.
