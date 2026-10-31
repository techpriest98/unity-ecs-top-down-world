# Stationary pole torches

## Recipes and interaction

- Active Flint Shard + offhand Large Branch: Craft Large Stick. Replaces the branch; costs 20 shard durability only on success.
- Active Large Stick + offhand Pine Resin: Craft Large Torch. Replaces the stick and consumes resin.
- Large Torch or Ignited Large Torch in active hand: Place (RMB) plants it upright on the selected Top, including lower-half supports, preserves instance durability and empties the hand. Full-height clearance and occupied anchor checks apply.
- Active Pine Resin + cursor on either stationary torch below maximum fuel: Refuel. One resin restores 20 durability, capped at maximum, then is consumed. Visibility carries the exact resolved entity into execution; no second target search.
- With active pine resin and a matching torch under the cursor, a `current / maximum` label appears above the torch, including `100 / 100`, regardless of interaction distance. Visibility/inspection accepts full torches; Execute still enforces its range and rejects refueling a full torch without consuming resin. Presentation reuses the resolved target and never executes Refuel. It hides on hand/cursor changes, missing target, pause or an offscreen anchor; its UI object and text are reused.
- Active Flint Shard + offhand Pyrite + cursor on a fueled Large Torch: Ignite. Replaces it with Ignited Large Torch, retaining durability; costs 20 shard durability.
- Burning uses the existing torch interval (1 durability per 4 simulation seconds). At zero fuel, it becomes an unlit Large Torch with zero durability, not destroyed. Refuel before lighting again. Chunk catch-up and saves preserve this state.
- In creative mode, both torch definitions are under Structures (with Structure selection semantics for Rubedo/Nigredo); Large Stick remains an Item. Pine retains its existing structure ID.

New item IDs: Large Stick 12, Large Torch 13, Ignited Large Torch 14. Skills 18–22.
No new instance fields/save format version are needed. Depleted world item records may contain zero durability; runtime definition policy decides whether to retain or remove them.

## Placeholder art

Generated with the built-in image generation tool, then alpha-cropped, nearest-neighbor fitted and alpha-thresholded by PrepareStationaryTorchSprites.ps1. All icons are physically 32x32. LargeStick_world is 32x16; upright torch world sprites are 32x64, not giant source files with only an import size cap.

Saved project files:
- Art/UI/Icons/LargeStick.png and LargeStick_world.png
- Art/UI/Icons/LargeTorch.png and LargeTorch_world.png
- Art/UI/Icons/IgnitedLargeTorch.png and IgnitedLargeTorch_world.png

Rebuild the manually assigned Item Database world atlas and assign the new asset to Item Library Authoring to batch these sprites with existing items. Until rebuilt, missing sprite entries use original textures without dropping the items.

Final generation prompts:

### Large Stick

Pixel-art inventory sprite for a dark earthy survival game, transparent background. ONE long straight peeled wooden staff, without bark or branches, pale tan hardwood with sparse dark brown pixels. Diagonal bottom-left to top-right, completely isolated, centered with generous clear margin. Crisp deliberate chunky pixel clusters as if 32x32 pixels enlarged, no antialias, no text, no ground/shadow, not a bundle. A large stick, not a twig.

### Large Torch

Pixel-art sprite for dark earthy medieval survival game. ONE UNLIT standing wooden pole torch, a tall peeled wood staff with a thick dark resin-soaked fiber head, primitive rustic outdoor torch like a wooden standing torch in Valheim. Fully upright straight vertical, front-view flat billboard, wooden shaft thick enough to read at low resolution. Centered, whole pointed bottom visible and top unlit resin head not clipped, generous transparent margin all around. Transparent background, no ground, no shadow, no pedestal, no text. Restrained brown/tan pixel clusters, sharp block pixels as if a 16x48 game sprite enlarged, no antialias.

### Ignited Large Torch

Pixel-art sprite for dark earthy medieval survival game. ONE LIT with a small orange-yellow pixel flame standing wooden pole torch, a tall peeled wood staff with a thick dark resin-soaked fiber head, primitive rustic outdoor torch like a wooden standing torch in Valheim. Fully upright straight vertical, front-view flat billboard, wooden shaft thick enough to read at low resolution. Centered, whole pointed bottom visible and top flame not clipped, generous transparent margin all around. Transparent background, no ground, no shadow, no pedestal, no text. Restrained brown/tan pixel clusters, sharp block pixels as if a 16x48 game sprite enlarged, no antialias.
