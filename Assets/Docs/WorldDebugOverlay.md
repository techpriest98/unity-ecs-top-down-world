# F3 world debug overlay

The existing persistent FPS overlay now shows FPS/frame time, world HH:MM, world-time multiplier, current wind strength and its natural target. It refreshes twice per second with TMP numeric formatting; cached ECS queries are rebound when the default World changes and disposed while their owning World is alive. No chunk/item traversal is added.

The fog row shows Auto / gathering, clearing, steady or clear; manual Full / Off; or unavailable when no usable fog renderer/sea/chunks exist. Percentage is the effective base opacity (weather density times configured Fog Opacity), not the average per-pixel noise coverage. F4 cycling no longer writes to the console. Text uses compile-time format strings and the existing twice-per-second update. Ordinary success/debug Log calls in Scripts/Tools were removed; error/exception reporting and missing-resource warnings remain. FogDebugStateChecks executes the real diagnostic properties with math adapters.

While F3 is visible and world startup is Ready:

- F5 doubles world-time speed; F6 halves it (0.25x..64x, default 1x).
- F7 increases current wind strength by 0.1; F8 decreases it by 0.1 (clamped 0..1).
- Simultaneous opposing keys cancel for their pair. Controls work in ordinary builds; hide the overlay to avoid accidental edits.

World-time speed is an ephemeral WorldTimeDebugSpeed component on the clock entity, not Unity timeScale. WorldTimeSystem computes scaled DeltaSeconds once and advances both Hour/UpdateTimer and monotonic SimulationSeconds with it. Wind, fog progression, shore animation, ground/equipped durability consume that delta; streamed-item catch-up already uses SimulationSeconds differences and must not apply the multiplier again. Future plant growth can use the same world clock. Physics, movement and character animation retain their normal rate. Pause/Rubedo still stop simulation. DeltaSeconds is transient and is zero during startup; the setting is not serialized and starts at 1 when the ECS clock/system is created. Elapsed world time itself is saved as before.

Wind adjustments change Strength once and preserve TargetStrength, target angle, RNG, drift and next-change timer. The normal wind system continues exponential relaxation (25 world-second time constant) and subsequent target selection. There is no persistent manual override. This intentionally lets the adjustment fade naturally rather than snapping back to a saved value. Simulation pause freezes normal clock/weather progression.

WorldDebugControlsChecks compiles real clock/wind/debug utility code with ECS/math adapters: clock acceleration, minute display/midnight, scaled SimulationSeconds/DeltaSeconds, paused/invalid deltas, frame partition, multiplier bounds, wind clamping, preserved target/RNG and recovery from stronger/weaker manual values. Existing WindChecks and WorldItemStreamingChecks pass. Full runtime C# compilation passes; actual Unity UI/hotkeys and scheduling require a play/build check.
