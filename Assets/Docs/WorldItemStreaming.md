# Chunk-owned world items

## Implemented behavior

WorldItemStreamingSystem keeps active ECS items only in generated terrain chunks. WorldItemChunk records the current X/Z owner using floor division, including negative coordinates. Moving/new items update ownership before motion; a crossing during motion is transferred next update. Sleeping items are considered for transfer only when loaded chunk membership changes. Stable fully sleeping frames skip transfer command-buffer work. Runtime support indices forget unloaded chunks.

When an item's positional chunk is absent, its current instance, position, velocity, gravity, radius, grounded state and accounted simulation timestamp move to a world-owned inactive dictionary. The entity is destroyed through an ECB, removing it from rendering, lighting, durability and ignition queries. Re-entering a generated chunk restores records once and removes the inactive bucket. Restored items omit runtime sleep tags and re-register support. Moving items in inactive chunks freeze their trajectory until loading; only time-driven durability advances while inactive.

This first implementation uses authoritative snapshots, not sparse deterministic-item deltas or persistent item GUIDs. The existing persisted CoastalItemChunks ledger remains the no-respawn/tombstone history: a processed empty chunk stays empty, and a picked-up or split generated Flint is not regenerated. Two shards are separate records; the destroyed parent is absent. Untouched generated items currently also occupy snapshot records in saves. A future compact origin-ID + delta format can reduce storage without changing this runtime streaming contract.

## Time and durability

WorldTime.SimulationSeconds is a monotonic double, advanced by world delta (simulation delta multiplied by the F3/F5/F6 world-time rate) only while startup is Ready. Existing pause and Rubedo UI disable SimulationSystemGroup, so it freezes; closed-game real time never enters the calculation. It is independent of wrapping hour-of-day and saved/restored with the world. Loaded durability uses the same scaled WorldTime.DeltaSeconds; inactive catch-up uses differences of SimulationSeconds without multiplying again.

Ordering: clock → active item durability → item throw creation/streaming → coastal spawns → sleep/motion. Streaming occurs in Simulation after Initialization terrain streaming. Active decay is applied before an unload timestamp is taken. Restoration happens after active decay for that frame; catch-up accounts through the current clock once. The next active frame resumes ordinary decay. Catch-up preserves DurabilityElapsed, interval and loss amount, using double arithmetic for elapsed intervals. A fully expired item is discarded on activation, matching active zero-durability destruction. Non-decaying items do no aging work. Wheat growth itself is not implemented, but the stored timestamp/world clock supports later time-based state transitions.

## Save compatibility

WorldStateStorage version is now 8. Versions 1–7 remain readable: their hour-of-day cannot reconstruct elapsed days, so accumulated time and existing item timestamps start a new epoch at zero, with no speculative offline aging. Version 8 validates finite, nonnegative world/item clocks and forbids item timestamps beyond the saved world clock.

WorldItems remains a flat compatible record array, now containing active snapshots plus inactive chunk records. A save captures active items at the current accounted clock and keeps inactive timestamps intact. Loading validates all definition IDs and imports records into inactive storage; distant items are not materialized just to unload them again. Existing world metadata, terrain modifications, equipment and generated-chunk ledger are retained. Missing runtime definitions keep a chunk's records instead of silently losing them. The existing atomic save/.bak policy is unchanged. Older game builds cannot read v8 saves.

## Verification and limits

Runtime and editor C# compilation pass. WorldItemStreamingChecks runs extracted production Coordinate/Save/Restore/CatchUp and active durability code with real Unity.Mathematics. Cases: negative/boundary chunks, full physics/resource snapshot state, active vs inactive aging, zero elapsed/paused interval, expiration, inert items and 200 fractional roundtrips. Static checks verify active+inactive save integration, import migration and sleep gates. Existing sleep, collision, spawn, Flint and item-render checks pass. Independent static review found no ordering cycles or apparent duplication path.

These are not Unity ECS integration or measured FPS results. No user saves have been rewritten by tests. Profiler marker: WorldItems.Streaming.

### Play-mode acceptance checks

1. Pick up Flint/Pyrite, leave far enough to unload the source chunk, return: no respawn.
2. Throw two items near a chunk boundary, leave/return: same counts, positions/state; thrown ownership follows actual position, not source chunk.
3. Split Flint, pick up one shard, unload/return: only the remaining shard exists, no whole parent.
4. Leave a lit torch, unload, advance play time elsewhere, return: expected reduced durability; a fully burned torch is absent. Pause/Rubedo and time with the game closed must not age it.
5. Save while torch/shards are inactive, reload, revisit: same states/counts, catch-up only for in-world elapsed time.
6. Load a pre-v8 save, save/reload with v8: equipment, modified blocks, remaining items and picked-item absence survive.
7. Check roofs/caves/full and half-block support after restore, removal of support, moving light cleanup on unload and restored light on reload.
8. Explore repeatedly: active WorldItem count should follow loaded territory rather than total explored territory; compare FPS and WorldItems.Streaming with the same settings.
