# Soil-dominant non-front Turf autotile rims — 2026-10-17

Built-in image generation edited the prior rim study to emphasize bare soil along the back and left/right top borders. Selected study: `Docs/References/Turf-Autotile-Soil-Rim-Study.png`. Final native overlay data is in the existing BlockAtlasDraft and runtime BlockAtlas color PNG. Composed preview: `Docs/References/Turf-Autotile-Color-Preview.png`.

The `-SoilRim` mode of `CompleteTurfAutotiles.ps1` recolors only existing opaque pixels of Top/Back, Top/Left, Top/Right, Outer TL/TR and Inner TL/TR. The complete front top band (destination y=12..15), its convex and concave corners, the Top/Front strip and every side overlay are protected. Thus the grass lip still continues into SideUpper exactly as before. All alpha masks, base textures, normal maps, unused slots and other blocks are unchanged.

Sampling is palette-restricted to existing Turf colors. The outer two exposed texels use existing brown soil colors; inner rim texels may retain olive transition tufts. 176 pixels changed (174 of the replacements are brown soil). Imported regions were verified against the draft and every other PNG pixel was checked unchanged. The actual production Compose passed all 1536 neighbour/above/foot combinations, exact convex corner/strip joins and concave transparency. In-game GPU lighting remains a visual check.

Full-file backups are provided here; if later unrelated painting exists, recover only the relevant Turf overlay pixels rather than restoring entire files.

## Built-in generation prompt

Use case: precise-object-edit. Image 1 is the edit target, a 32x48 logical-pixel pixel-art terrain texture enlarged to 1024x1536. Change ONLY the BACK and LEFT/RIGHT exposed edges of its TOP surface (logical y 0..15). Make the outermost 3 logical rows at the top/back and the outermost 3 logical columns on each side of the grassy top substantially MORE BARE BROWN EARTH and LESS grass: irregular chipped dirt with angular cracks, a few remaining olive tufts, not a pale grass frame. At least two thirds of these exposed rim strips should be brown dirt. Use the brown soil colors already visible on the lower side, no new palette. Preserve the grassy central area and the entire FRONT lip of the top (logical rows 12..15) EXACTLY AS IS, because that edge must flow into the grass cap of SideUpper. Preserve ALL lower side pixels (rows 16..47), including grass lip, stones and cracks. Match the same coarse 32x48 logical grid; no fine detail, no gradients, no smooth blending, no rendering, no perspective. Only existing muted brown, olive, straw and grey flat colors. Single full-bleed 2:3 rectangular image with identical layout. No text, labels, borders or extra objects.

