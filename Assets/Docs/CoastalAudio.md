# Coastal audio

## Player audio settings

Intro sources use the same base levels as gameplay: musicVolume for the instrumental and seaVolume for sea ambience (the maximum/closest-shore level, no distance attenuation). AudioCategoryVolume then multiplies each by its corresponding saved Music/Ambience setting. Other scenes retain their authored source balance.

Opening Rubedo freezes simulation without pausing coastal music or ambience. Other simulation pauses (including the pause menu) still pause both sources; closing Rubedo does not restart the tracks.

Both MainMenu and WorldScene have a SettingsMenu controller. It instantiates the existing UIPanel/ListButton prefabs at runtime, preserving the host panel's placement, frame and typography, and uses the same SetActive navigation as existing menus. MainMenu's SettingsButton opens Settings → Audio. Pause adds a Settings entry beneath Resume/Return to Main Menu. Back/Escape returns Audio → Settings → originating menu; gameplay remains paused until Resume. Sliders support pointer/drag and Unity Selectable keyboard navigation.

Music and Ambience sliders range from 0–100%, default 100%. They multiply the authored clip/biome volumes rather than replacing distance attenuation or fade envelopes. PlayerPrefs keys `Audio.MusicVolume` and `Audio.AmbienceVolume` persist across scenes/restarts; Back, application pause and quit flush changes to disk. Values are validated/clamped. The same category settings apply to Intro's original clip sources via AudioCategoryVolume, preserving their authored relative volumes. Coastal runtime sources apply category gain separately, avoiding double multiplication or compounded fade calculations. Gameplay audio remains paused while adjusting settings from pause; its mix is updated and heard on Resume.

`AudioSettingsChecks.ps1` tests the production preference logic using an isolated in-memory PlayerPrefs adapter (not the developer's real preferences), including defaults, notifications, persistence reload, mute, clamp and invalid values. It validates scene object IDs and prefab links. Runtime C# compilation passed; actual UI layout/drag/escape and listening still need Unity Play-mode verification.

`Resources/CoastalAudioSettings.asset` references the same two clips used by Intro, without moving or modifying either original asset:

- Rocky Shore music: `Audio/sea-shore (2).mp3`, volume .35.
- Sea ambience: `Audio/oceanwaves.wav`, volume .75.

The automatic presentation system creates two looping 2D AudioSources only after the player and WorldStartupState.Ready exist. Music uses WorldMinimapInfo/WorldMinimapCell with the same UV conversion and nearest-cell rounding as EquipmentHud.SetMinimap's regional biome label, not per-column terrain biomes. Missing or incomplete map data preserves the previous music target; valid coordinates outside the map resolve to Ocean, matching the HUD. Thus moving onto a local ocean/sand column does not mute music while the regional label remains Rocky Shore. Both tracks fade smoothly, stop when silent and restart on re-entry. Playback is paused with the simulation; runtime sources are destroyed when gameplay readiness disappears or the ECS world is disposed. No scene setup or additional AudioListener is required; the game's existing listener is used. Original Intro assets are unchanged.

Sea volume uses horizontal world-space distance to the closest loaded OceanWater contact produced by the same BuildShoreMask routine as Shore Waves, including diagonal rock corners and half-height surfaces. Full volume within 3 blocks, smoothstep fade to silence at 32, on both water and land sides. This does not model cliff-height attenuation or acoustic occlusion. Contacts outside loaded chunks cannot contribute.

The search runs at 4 Hz over cached surface columns in chunks intersecting the audible radius; smoothing runs each presentation frame. It does not regenerate terrain, scan full-height voxel columns, instantiate per-wave emitters or calculate anything per audio sample. Chunk block reads only resolve contact material through the existing accessor. Inspector ranges cap the search radius at 64 blocks. Real runtime CPU/audio costs have not been profiled.

Settings expose the clips, two volume levels, full-volume distance, silence distance and fade time. Fade time is the duration for a normalized 0→1 gain change. Audio clip import/loop seams remain as in the original assets; listen in-game to assess balance and loop quality.

Verification: runtime C# compilation passed. `Tools/ItemGraphEditor/Editor/CoastalAudioChecks.ps1` checks actual distance/gain code, edge and corner mapping, equal inland/ocean falloff, negative world coordinates, monotonic gain, radius endpoints and clip GUID references. Listening and scene-transition behavior still require Unity Play mode.
