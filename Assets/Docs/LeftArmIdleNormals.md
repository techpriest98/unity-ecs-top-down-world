# Left arm idle normal draft

Asset: Art/Player/Animations/LeftArm/left_arm_idle_normals.png (128x512, 2x4 cells of 64x128). Original diffuse and animation definition are unchanged; assign NormalMap after visual review. Import: linear raw RGB, readable, Point, no mipmaps/compression, Alpha Is Transparency off.

Built-in imagegen was guided by left_arm_idle.png and body_idle_normals.png. Proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-8eb5b301-02ef-436a-83c4-c981cec4eba2.png

Postprocess by PrepareLeftArmIdleNormals.ps1: register proposal arm bounds to each original silhouette, restrict to exact editor palette by angular nearest normals, preserve every original alpha (including black outline pixels), retain the wholly empty fourth row. Generated glow is excluded by alpha/brightness registration threshold. The generator incorrectly mirrored second-column arms; first-column proposal is reused for both matching Idle silhouettes to avoid opposite lighting. Nearest horizontal valid normals extend to original silhouette edges; unmatched pixels default to front/vertical center. Bounding-box registration is approximate; source alpha is exact but anatomy needs in-game review. Final reviewed candidate v2 is copied to the main asset; v2 retained as intermediate.

Validation: 1,924 visible pixels, frame counts 321/322,270/270,374/367,0/0. Entire saved PNG verified, visible RGB all in picker palette. No shader/material changes.

## Exact generation prompt

+Use case: precise-object-edit. Create a pixel-art NORMAL MAP for the isolated LEFT ARM idle sprite sheet in image1. Image2 body_idle_normals is a normal direction/style reference, not extra geometry. Original image1 is 128x512 with 2 columns x4 direction rows of 64x128 cells. Only first three rows have arms; bottom row is intentionally empty and must stay empty.
Output ONLY SIX isolated arm normal patches in a 2-column x3-row grid on genuine transparency. Each pair follows the respective original pose: front, back, left profile. Do not include torso/head/body, no extra limbs or fingers. Preserve the original slim relaxed hanging arm, small fist and sleeve at upper end. Source silhouette/pixel shape is authoritative. Identical geometry between frames should have consistent normals.
Anatomy: simple broad cylindrical upper arm and forearm, elbow plane transition, wrist and small relaxed hand. Most shaft surfaces are VERTICAL with forward/side blue-violet normals, NOT green/yellow up-facing just because diffuse skin is bright. Up-facing normals only on actual shoulder/sleeve top and top knuckle shelf; down-facing where underside geometrically requires it. Small olive sleeve should match source cloth volume. No exaggerated muscles, no noisy microdetail. Do not interpret black outlines as deep grooves or pigment shading as geometry.
Coordinate basis X image-right, Y world-up, Z toward camera. ONLY these 12 exact RGB hex: 36E880 80FF80 C9E880; 25C0BF 80DAD9 DAC0BF; 3680E8 8080FF C980E8; 3636C9 8025DA C936C9. Crisp nearest-neighbor pixel blocks, no antialiasing, gradients, baked lights, labels, margins or background.
