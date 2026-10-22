# Walk hair normals, lower three rows only

Updated Art/Player/Animations/Body/body_walk_normals.png in place as requested. Backup of the exact input PNG: body_walk_normals_before_hair.png in this folder. Metadata and animation references are unchanged.

Built-in imagegen provided twelve hair-only proposals, using body_walk for silhouettes/strand design, body_idle_normals for normal volume/style, and the current body_walk_normals as the target reference. Raw proposal: C:/Users/Андрій/.codex/generated_images/01a11268-95c2-7543-bc93-de802a69f0c1/exec-0ab8bde5-ba18-452c-a153-5c06d7625150.png

PatchWalkHairNormals.ps1 registers hair bounding boxes per frame, samples nearest-neighbor into the source grayscale scalp mask, and snaps decoded normals to the current twelve-color editor palette. Existing normal alpha is retained. Transparent proposal omissions retain their prior RGB. First row y0..127 and all pixels outside the lower-row hair mask remain exactly unchanged; the saved PNG is compared to the verified result. Original black ink and warm skin are excluded. Profile lower-face windows avoid beard/eyes; automated fringe segmentation and registration still warrant artistic review.

Rebuild the animation atlas if this map is assigned to a clip; atlas subassets do not change merely because the PNG changes.

## Exact prompt

+Use case: precise-object-edit. Produce SCALP HAIR ONLY normal-map patches for the twelve walk sprites in the LOWER THREE ROWS of image1 body_walk.png. Image2 body_idle_normals.png is the authoritative normal style and hair-volume reference; image3 current body_walk_normals.png is the edit target, whose FIRST ROW IS FINISHED and must not be redrawn.
Output twelve isolated hair silhouettes in a 4-column by 3-row grid: back-facing hair on row1, left-facing on row2, right-facing on row3. Same walk phase order as image1. Do not output first/front-facing row. Hair only, no beard, face, eyes, neck, skin or clothes. Transparent background, no checkerboard.
Follow exact pixel locks and grayscale hair strand details of original artwork. Preserve hairstyle/parting, silhouette and orientation of every pose. Broad skull volume from Idle normals with individually sloping hair clumps/ridges, no treating dark pigment as depressions. Consistent normals across same-direction frames. Hard pixel art edges, no antialiasing or soft gradients. Use ONLY exact palette RGB hex: 36E880 80FF80 C9E880 (up); 25C0BF 80DAD9 DAC0BF (45 up); 3680E8 8080FF C980E8 (vertical); 3636C9 8025DA C936C9 (45 down). Coordinate X screen-right, Y world-up, Z toward viewer. No text or borders.
