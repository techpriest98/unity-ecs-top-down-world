# Left arm diffuse cleanup

Updated Art/Player/Animations/LeftArm/left_arm_idle_diffuse.png and left_arm_walk_diffuse.png in place. Exact pre-edit PNGs are preserved here as left_arm_idle_diffuse_before.png and left_arm_walk_diffuse_before.png. Existing metadata/GUIDs and animation references are unchanged.

The first top-left frame of each file is the author's finished example and remains RGBA-exact. Other frames use the same base skin #A2968B and sleeve #817D6A. Material-specific warm ramps are flattened; dark ink, all original alpha/transparent RGB, empty cells, source pose shapes and pixel positions are preserved. No generated geometry is substituted.

Built-in imagegen was used for cleanup proposals. Final production pixels are deterministic material-ramp replacements by CleanLeftArmDiffuse.ps1 to avoid redrawn silhouettes. Each saved PNG is compared to the verified result. Idle changed counts: 0,160,127,127,164,161,0,0. Walk: 0,156,141,156,202,127,139,127,219,164,221,164,101,0,0,0.

Rebuild the animation atlas after assigning these diffuse files as SpriteSheet. Re-trim only if source silhouette bounds have changed; this cleanup does not change alpha.

## Exact proposal prompt (same for each sheet)

Raw proposals (not substituted for source geometry): C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-ed017340-e31f-4a12-b520-fca49d1b863c.png (Idle); C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-50617b83-d299-4b4a-88f7-9867f824814d.png (Walk).

Use case: precise-object-edit. Clean diffuse albedo for this isolated left arm sprite sheet. First frame at top-left is finished and authoritative: skin #A2968B, cloth #817D6A. Remove remaining skin and sleeve light/dark shading in other frames to those exact flat base colors. Preserve first frame EXACTLY, original black/dark ink, all silhouettes and poses, cell layout and alpha. Do not redraw anatomy, add muscles, glow, textures, outlines or missing arms. Genuine transparent background. No text, no margins. Pixel art.
