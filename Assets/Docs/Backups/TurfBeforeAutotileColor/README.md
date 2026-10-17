# Turf autotile color maps — 2026-10-17

The user’s latest Turf base textures were inspected alongside their Stone and Sand overlay examples. A built-in image-generation edit produced a rim study; deterministic coarse-grid sampling, exact palette restriction and native-layout packing then built all 20 color overlays for Turf (ID 9). No CLI image-generation fallback was used.

## Saved assets

- Current editor data: `Tools/BlockAtlasEditor/Editor/BlockAtlasDraft.asset` (only Turf color overlay rectangles).
- Runtime color texture: `Art/World/BlockAtlas.png` (same rectangles).
- Selected generated study, retained in the workspace: `Docs/References/Turf-Autotile-Edge-Study.png`.
- Final composed preview: `Docs/References/Turf-Autotile-Color-Preview.png`.

The study is a visual source, not the final atlas: it may contain intermediate colors. The final packed overlays use ONLY exact opaque RGB colors already present in the current Turf base textures, with binary alpha. The base Top, SideUpper and SideLower pixels, normal maps, unused atlas slots and all other blocks are unchanged. No prior generation/completion scripts were rerun over the user's hand edits.

## Construction and checks

`CompleteTurfAutotiles.ps1` reads the current native BlockAtlasLayout rather than hardcoding packed source coordinates. It samples the selected study at 32x48 logical resolution, snaps to the existing palette, and extracts coherent master edges for strips and convex corners. Irregular thin masks use the existing edge-material pattern; concave corners are small three-pixel notches. There are 620 opaque pixels across 20 overlay elements. Empty original Turf overlays were required before import; the script refuses to overwrite authored nontransparent overlays or an existing backup.

`TurfAutotileChecks.ps1` evaluates the actual production BlockAtlasLayout.Compose using headless Color32/RectInt adapters. Passed all 1536 combinations (256 neighbour masks × two above states × no/Turf/Stone foot), exact convex-corner/straight-edge color and mask joins, and concave transparency. Import verification also checks palette membership, unchanged base/normal/other-block data, and whole-PNG preservation outside target rectangles. Actual GPU rendering and in-game lighting are not asserted. Overlay normal-map art remains a separate future task.

Full-file backups are retained here. After later changes, recover only Turf color overlay rectangles instead of restoring whole files over unrelated painting.

## Built-in generation prompt

Use case: precise-object-edit. Asset type: low-resolution pixel-art terrain autotile edge study. Image 1 is the EDIT TARGET: exact 32x48 logical pixels enlarged 16x, showing top grass in rows 0..15 and a vertical soil side in rows 16..47. Image 2 is SUPPORTING REFERENCE: three packed atlases, turf, stone, sand left-to-right, showing how the user's stone and sand edge overlays work. Produce a SINGLE FLAT RECTANGULAR texture study, 2:3 aspect ratio, exactly the same composition as image 1. Edit ONLY the outermost 3 logical pixels of the 32x48 rectangle and the 3-row grass-to-soil lip at rows 16..18. Make these into tasteful terrain edge artwork: a subtle irregular olive grass rim with sparse pale dry grass blade tips along top/back and left/right top edges, a few hanging olive/straw tufts on the front grass-to-soil lip, dark soil chips and cracks along left/right vertical side edges, and an understated broken brown soil foot along the very bottom. The top FRONT edge is logical rows 12..15: give it a restrained, irregular olive/straw lip coherent with the grass above the soil. Everything remains inside the original rectangular 32x48 bounds; no protrusions, no perspective, no isometric cube, no bevel sculpture, no 3D rendering, no cast shadow. Keep the original center textures unchanged. Match the user's COARSE logical pixel size and simple angular shapes: every logical pixel a flat square, NO subpixel detail, NO antialiasing, NO gradients. Use ONLY existing muted olive, straw, brown and grey colors sampled from image 1. Preserve the large pattern of the user's grass and the side's cracks and sparse stones; don't replace it with dense decorative noise. This is an albedo/color texture to supply consistent crop strips for an autotile atlas, NOT a normal map. No text, labels, grid, frame, padding, background or extra items. Full-bleed rectangular output. Image 2 only guides edge treatment, do NOT paint stone/sand materials onto the turf.

