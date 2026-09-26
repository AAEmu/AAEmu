# GF-E07A fishing closure

This slice closes the regular rod-casting admission and reel-up failure paths only. It does not change sailing-activity rewards, requests, or UI.

## Evidence used

- The shipped rod skills use plots 809 and 821, are position-targeted, and carry `target_water` / `target_only_water` in the skill content.
- Both rod rows are non-cancelable for casting and channeling. The existing stop path therefore refreshes the last rod event instead of tearing down a live cast.
- Plot cancellation already emits the existing plot stop replies (`SCPlotCastingStopped` 0xE6 and `SCPlotChannelingStopped` 0xE7). A dropped sport-fish line requests that cancellation.
- Regular reel-up loot is special effect 79 (`FishingLoot`). It selects the zone-group land or sea pack and delivers through the existing `LootPack` path; there is no fishing-specific loot container packet to add.

## Implemented behavior

1. **Start** — a rod plot with `target_only_water` is admitted only when the resolved target position is inside the world water volume. A dry target returns the existing `InvalidTarget` skill result, which follows the normal `SCSkillStarted` failure reply.
2. **Stop** — the existing non-cancelable rod rule remains authoritative. A client stop cannot interrupt either shipped rod plot while its cast/channel is active.
3. **Close** — the existing plot cancellation and line-break path remains the close path; no new fishing packet or UI state is invented.
4. **Inventory full** — if the content-selected `LootPack` refuses delivery, the existing `BagFull` error reply is sent. Missing target/pack content is reported as `InvalidTarget` / `Invalid` instead of silently returning.
5. **Delivery** — successful reel-up continues to use `LootPack.GiveLootPack`; no sailing reward or alternate delivery route is introduced.

## Deliberately blocked

The sailing-activity family remains out of scope. Its request/reward packets and activity-content relationships require separate evidence and a separate slice. No sailing IDs, rewards, or UI behavior are inferred here.

## Verification

- `FishingStartRulesTests` covers both shipped rod plots, dry targets, non-rod plots, and the flag gate.
- `FishingLootReplyRulesTests` covers invalid target, missing/empty pack, bag-full, and success replies.
- Existing sport-fish tests continue to pin the stop and dropped-line close behavior.
- Build and test verification is recorded in the parent task result.
