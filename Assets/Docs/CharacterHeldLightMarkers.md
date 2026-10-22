# Held item light markers

Automatic placement and preview share SpriteOffset keyed by PlayerSpriteDirection (not a cast to PlayerFacingDirection, whose horizontal values have different order). Facing screen-right places the left emitter toward camera and right emitter away; facing screen-left is the reverse, for every camera View. An attempted reversal of profile ZIndex signs caused a visual regression and was reverted: original authored sprite sorting is retained independently of emitter depth. Authored LightPosition values were not changed. Allow subscene rebaking/re-enter Play Mode; no texture atlas rebuild is needed. Tests cover all four camera views and facing directions. Actual scene emitter positions still require Unity Play Mode verification.

Open Tools > Character Animation, assign the animation and item databases, equip a light-emitting item. Enable **Show light markers (drag to edit)** above the preview. Left-hand emitter is yellow, right-hand emitter cyan. Markers are editor handles only, not an illuminated rendering preview.

Select direction and Idle/Walk, pause or select a frame. Drag the cross to set its pixel X/Y. Dragging pauses playback automatically and enables the custom position. Under the corresponding item in the left panel, **Custom position** toggles the override and **Pixel X/Y + depth** edits all three coordinates. X/Y are source-cell pixels from bottom-left of the 64x128 cell. Z is world units toward the camera, and cannot be edited by the 2D drag. **Copy to all frames in this direction** copies only anchor settings in this clip/direction, never trim bounds or ZIndex. Undo is supported and edits are saved to the AnimationDefinition asset.

This is per held-item animation clip, per frame, per direction and per hand (separate CharacterPart clips). Items sharing that same clip share its anchors. It does not modify the item Light Definition's color, radius/intensity or ground-item emitters.

Without an override the marker displays the existing approximate held emitter, and runtime behavior is unchanged. Custom X/Y is converted by player height/98 with the same upright-sprite baseline Y=25 and center X=32 used by normal lighting. Screen right/depth map into the current camera's world basis; sprite direction is resolved from player facing and view. This is an approximate upright normal-lighting plane, not exact perspective projection.

Anchors bake from the current definition into PlayerAnimationFrameBlob independently of atlas pixels. Rebuilding texture arrays is not required for an anchor-only edit. Unity must finish rebaking the player subscene; re-enter Play Mode if it does not refresh live. Ground-item emitters are unchanged. FrameLayers preserve anchor metadata through Trim All's existing struct copy.

PlayerEquipmentLightSystem now runs after PlayerAnimationSystem, before DynamicLightSystem, so item clip selection and playback frame match the visible sprite without a one-frame lag. Non-emitting/hidden items are not edited by preview markers. Runtime still requires a valid light source and visible item frame before applying overrides.

Verification: runtime and editor assemblies compiled, PlayerNormalTestChecks exercises anchor baseline and four camera bases/depth. Native Unity window interaction, subscene rebaking, and final in-game appearance require editor testing.
