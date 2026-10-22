# Left arm bent Idle maps

New sibling assets under Art/Player/Animations/LeftArm:
- left_arm_bent_idle_normals.png: raw linear RGB, readable, Point, uncompressed, no mipmaps or Alpha Is Transparency processing.
- left_arm_bent_idle_diffuse.png: clean albedo with inherited diffuse importer/slicing.

Original left_arm_bent_idle.png and animation references remain untouched. Assign these to LeftArmBentIdle.NormalMap / SpriteSheet and rebuild the animation atlas after review. Both maps are 128x512 (2x4 cells), retaining the original alpha at every pixel and all eight poses.

Built-in imagegen was used for normal anatomy proposals and a diffuse cleanup proposal. Normals follow the approved left_arm_idle_normals simple plane style, adapted for the bent elbow and horizontal forearm. PrepareLeftArmIdleNormals.ps1 -Bent registers per-row generated bounds onto exact source pixels and snaps decoded normals to the 12 editor palette colors; matching Idle pairs reuse the proposal's first column for consistent orientation. Approximate registration and edge extension still require in-game visual review. 2,440 visible pixels (264,262,249,249,445,445,263,263); 408 outline extensions. Entire saved PNG verified.

Diffuse final pixels use deterministic material-ramp cleanup by PrepareBentArmIdleDiffuse.ps1, not generated geometry: skin #A2968B, cloth #817D6A. Original dark ink/transparent RGB and every alpha are retained. Changed counts: 141,145,113,113,275,274,136,136.

Normal raw proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-b266ba24-47b6-4794-9747-6e8613ff1991.png

## Normal prompt

undefined

## Diffuse prompt

undefined
