# Graphics settings

Both main-menu and pause-menu SettingsMenu now generate Settings > Graphics using the existing audio-slider styling (ivory track/diamond handle). Chunks Loading Radius is an integer slider from 1 to 4, default 1, displaying the number without a percent suffix. Long graphics label auto-sizes to fit the same 380-unit width. Back/Escape returns to Settings and selects Graphics again; Resume/Close hides all settings subpanels. Generated graphics panel is destroyed with its owner.

GraphicsPreferences stores Graphics.ChunksLoadingRadius in PlayerPrefs. Changes update the cached integer immediately and are flushed on Back, Close, application pause or quit alongside audio preferences. Stored/input values clamp to 1..4. No ECS world is required to set this in the main menu.

ChunkStreamingRadiusSystem reads the preference before ChunkStreamingSystem and publishes it to both ChunkStreamingSettings and runtime radius. Runtime changes are applied on the next initialization streaming update, with existing creation/destruction budgets. No camera/zoom radius override is active. Radius now means the TOTAL chunk-coordinate neighbourhood, including the outer preparation ring: 1 => 3x3 (9), 2 => 5x5 (25), 3 => 7x7 (49), 4 => 9x9 (81). No extra preload or unload ring is added. Center-first creation still prepares the outer ring before the player reaches it. Initial spawn search retains its safety expansion but checks the same total radius; once spawning completes, the selection is restored. Movement/radius reduction/startup can temporarily leave extra retiring chunks until budgeted removal finishes. Higher values substantially increase memory/loading work. Lighting GPU radii and world-save data are unchanged.

Checks: runtime compilation and GraphicsSettingsChecks actual preference code with isolated PlayerPrefs fixtures (does not modify user settings), navigation/slider/streaming source contracts. AudioSettingsChecks regression. Visual layout, dragging/four steps, controller/keyboard and pause/main-menu interaction still require Unity verification.
# Detailed lighting radius

Graphics also provides an integer `Detailed Lighting Radius` slider (8–16 world blocks, default 8).
It applies live to detailed local terrain/water lighting and the optional GPU-sun region, independently of chunk loading radius.
Preferences persist via `Graphics.DetailedLightingRadius`. Occupancy and sun caches resize/invalidate when needed.
Local emitter range (maximum 8), the eight-source limit and legacy-light fallback remain unchanged.
Higher settings increase geometry-cache rebuild and GPU work; geometry outside loaded chunks remains unknown, not transparent.

