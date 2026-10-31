## Torch visual revision

The unlit/lit torch world sprites and inventory icons were revised with built-in imagegen (not the CLI). Large Stick is unchanged.

Saved assets, retaining their original GUIDs:
- Art/UI/Icons/LargeTorch_world.png (32x64)
- Art/UI/Icons/IgnitedLargeTorch_world.png (32x64)
- Art/UI/Icons/LargeTorch.png (32x32)
- Art/UI/Icons/IgnitedLargeTorch.png (32x32)

Both states use the same unlit-reference registration in PrepareRegisteredTorchSprites.ps1. Wood body occupies logical rows 15–62, leaving room above for flame; the lower shaft silhouettes match exactly. Source Read/Write settings and item definitions are unchanged. Rebuild the saved atlas manually to see the revised pixels in an already packed atlas.

Unlit source retained at C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-d0f07216-c477-422b-92fe-47469043d8cb.png
Lit source retained at C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-83ba9688-31fd-44d6-be84-2cc0a0962c1c.png

Final edit prompt (unlit; existing sprite = edit target, screenshot = style reference only):

Use case: precise-object-edit. Image 1 is the existing unlit pole-torch sprite to redesign. Image 2 is an in-game screenshot used ONLY as pixel-art style reference, not an edit target. Output ONLY ONE isolated redesigned UNLIT pole torch on genuine transparent background. Keep the upright front-view billboard and tall slender survival-game prop silhouette, but make it fit the restrained gritty pixel-art world: narrow irregular peeled ash-brown wood shaft with subtle grain, not regular bright horizontal ladder stripes; compact elongated bundle of resin-darkened fibers secured by two thin natural bindings at the top, not a mushroom/basket or giant spherical head. Muted desaturated earthy brown and tan palette, dark fine outline, subtle left light, no bright gold metal, no guard, no pedestal. Work as crisp 32x64-pixel game sprite enlarged with nearest-neighbor style, sparse deliberate one-pixel clusters, no painterly blur, no glow, no scene, no text. IMPORTANT FIXED REGISTRATION: canvas logical 32x64; centreline x=16; pointed wooden foot at y=62; resin head top around y=15, head width about 7 pixels, shaft width about 3 pixels; preserve transparent empty space in top 14 rows reserved for a future flame. Do not trim the canvas. The lit variant must later use precisely the same wood body and base registration.

Final edit prompt (lit; revised unlit sprite = edit target):

Use case: precise-object-edit. This is the UNLIT pole torch sprite; create its LIT state by editing ONLY the resin head and the transparent space immediately above it. Preserve the canvas size, exact torch position, centreline, shaft length, pointed foot position, all wood pixels below the head, and all scale/proportions. Add a compact asymmetrical upright orange/amber flame with a pale warm yellow core rising above and partially wrapping the small resin head. Match the existing sparse crisp pixel clusters. Keep the flame about 10–12 logical pixels tall in this 32x64 logical sprite, without floating sparks, smoke, haze, bloom, radial glow, shadows or any background. Body must NOT become shorter, thicker, more golden or move upwards. Keep alpha transparency. Output just this ONE lit variant registered identically to the input.
