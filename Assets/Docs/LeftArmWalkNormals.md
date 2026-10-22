# Left arm walk normals

Output: Art/Player/Animations/LeftArm/left_arm_walk_normals.png. 256x512, four columns and four directions. Original diffuse and animation references are unchanged. Assign this raw RGB normal map to LeftArmWalk.NormalMap and rebuild the animation atlas for in-game review. Import metadata is linear, Point, readable, uncompressed, no mipmaps, Alpha Is Transparency off.

Built-in imagegen used left_arm_walk.png as pose/silhouette source and the approved left_arm_idle_normals.png as normal style reference. Proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-40644d51-ada4-4fa8-96b7-1ed07ee5c200.png

PrepareLeftArmIdleNormals.ps1 -Walk registers each individual pose to its source bounding box, restricts RGB to the editor's twelve normal colors using angular nearest matching, extends valid edge normals horizontally where the generated silhouette is narrower, and uses vertical-center fallback if a proposal row is entirely missing. This is approximate anatomical registration but exact source alpha, including the visible fourth-row first-cell fragment and remaining empty cells. Saved PNG is checked against the verified result; source images are untouched. Final lighting/animation continuity requires Play Mode review.

## Exact generation prompt

+Use case: precise-object-edit. Generate a raw RGB normal-map sprite sheet for image1 left_arm_walk.png (256x512; four columns x four direction rows of 64x128 source cells). Image2 left_arm_idle_normals.png is the APPROVED normal-map style and anatomy reference.
Match every walk pose in image1, exact phase order, limb slope, elbow, wrist, small fist and sleeve. Image1 fourth row has only a short visible arm fragment in its first cell; other empty cells must remain empty. DO NOT add missing arms, torso, head, weapons or fingers.
Output four-by-four aligned cells on genuine transparent background. Use broad simple arm volume as in approved image2, not detailed musculature: forward-facing vertical surfaces mostly blue, side planes blue-violet, shoulder/sleeve tops and knuckle top shelves cyan/upward. Adapt slopes to swing pose, don't infer up direction from bright skin. Corresponding directions must not mirror normal X between phases unless actual surface orientation changes.
Normal basis X screen-right, Y world-up, Z toward viewer. Exact palette ONLY: 36E880 80FF80 C9E880; 25C0BF 80DAD9 DAC0BF; 3680E8 8080FF C980E8; 3636C9 8025DA C936C9. Crisp low-resolution pixel blocks, no antialiasing, NO glow or colored haze, no gradients or baked lighting. Exact source silhouette, no text, labels, checkerboard, margins.
