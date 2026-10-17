# Exact Turf normal palette — 2026-10-17

Snapshot before restricting all 64x48 Turf (ID 9) normal RGBs to the current editor's twelve picker swatches. 1866 pixels changed, of which 1354 have nonzero alpha. All 3072 RGBs, including invisible texels, now belong to the exact picker palette. Existing valid colors and every alpha byte are preserved.

`Tools/BlockAtlasEditor/Editor/NormalizeTurfNormalPalette.ps1` extracts the current DrawNormalPicker expression and evaluates it using float32 vector/color/rounding adapters. Its intermediate Color32 rounding is retained (the center 45-up swatch is currently RGB 128,218,217, not the idealized 128,218,218). Pixels are assigned by maximum dot product of normalized vectors, not nearest display-space brightness. No dithering or intermediate blending is used.

Only Turf's normal data and its runtime normal atlas rectangle changed. All color data, other draft entries and other normal atlas rectangles are unchanged. Full-image PNG verification, draft/runtime parity and exact palette membership passed; a second `-CheckOnly` pass also passed. All 32 SideUpper/SideLower seam normals still match, and the existing four-view shader normal basis regression passed. The preview was refreshed from final data.

To audit without writing: `Tools/BlockAtlasEditor/Editor/NormalizeTurfNormalPalette.ps1 -CheckOnly`.

Earlier completion scripts and backups describe the pre-quantization stages and may contain intermediate vector colors. They should not be rerun over the finished asset. Full-file backups are retained here; recover selectively if newer unrelated painting exists.
