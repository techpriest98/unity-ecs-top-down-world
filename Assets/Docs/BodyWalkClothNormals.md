# Walk cloth normal draft

Created using the built-in image generation tool, guided by body_walk (folds and pose), sprite_walk (macro form), and body_idle_normals (preferred vertical-plane cloth style).

Outputs: Art/Player/Animations/Body/body_walk_normal_cloth.png is clothing only; body_walk_normals.png composites that clothing onto sprite_walk_palette, preserving original baseline alpha and non-cloth RGBA. Both sheets are 256x512, sixteen 64x128 cells. Inputs are unchanged. BodyWalk's runtime reference is not replaced automatically, so the draft can be reviewed before adoption.

Postprocess: PrepareWalkClothNormals.ps1 uses the source tunic material-color mask, per-cell cloth bounding-box registration, nearest angular picker-palette restriction, and baseline fallback for proposal omissions. This preserves source pixel placement but is approximate registration, not a guarantee every generated fold is artistically correct. Black outlines/belt and exposed skin are excluded from the cloth classifier.

Raw proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-4c58223b-8b0b-4121-881a-c8473ceb2065.png

## Exact generation prompt

+Use case: style-transfer. Edit target image1 body_walk.png (256x512 sprite sheet, four columns x four rows, 64x128 cells); image2 sprite_walk.png is the broad normal geometry reference, image3 body_idle_normals.png is the authoritative cloth normal style reference.
Create ONLY CLOTHING normal maps for all sixteen walk poses. Transparent everywhere outside the tunic/cloth, including head, exposed neck/chest skin, legs, feet and arms. Keep the same four-by-four cell layout, pose-specific tunic silhouettes and folds from image1. No rearrangement. Match image3 clothing: broad forward/vertical blue planes with decisive cyan upward fold ridges and violet/magenta side planes; do not make the whole tunic green/up-facing from original brightness. Image2 provides macro form, image1 provides actual cloth folds; brightness is not surface orientation. Stable corresponding fold directions across walk frames, retain pixel art hard edges, no antialiasing, no lighting baked into output.
Encode normals X right, Y world-up, Z toward viewer. Use only these RGB hex colors: 36E880 80FF80 C9E880 (up); 25C0BF 80DAD9 DAC0BF (45 up); 3680E8 8080FF C980E8 (vertical); 3636C9 8025DA C936C9 (45 down). Favor vertical planes like image3. No text, borders, checkerboard or background. Output complete 4x4 transparent clothing-only sheet, exact same relative cell placement as image1.
