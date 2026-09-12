# Farmhand farming

## Player flow

1. At the management interaction for a completed house you own, bind your Farmhand to that house.
2. Move an eligible garden blueprint from your bag into the Farmhand's garden storage. Eligibility, land or water
   area, and required harvest grade come from the blueprint's normal housing content.
3. Charge the Farmhand with enough Labor and Vigor for the work you want to register.
4. Register a supported crop or livestock harvest and choose its requested amount. You can cancel an active job,
   although cancellation does not refund its spent Labor or Vigor. Completed harvests are delivered by mail even
   when the owner is offline.
5. Use the Farmhand garden expansion action when you meet the content-backed level and item requirements for the
   next farming slot.
6. Releasing the Farmhand cancels its active jobs and returns the exact stored garden blueprint items by mail.

## Supported scope

Farmhand garden eligibility reuses the normal housing-design path in `HousingGameData`: `item_housings` identifies
the blueprint's design, while `housings` and `housing_sizes` provide its Farmhand harvest grade and garden area.
This leaves normal placement and use of those Garden designs on the existing housing path.

Placed crops and livestock continue to use the existing generic doodad lifecycle: `DoodadFuncGrowth`,
`DoodadFuncUse`, `DoodadFuncRatioChange`, and `DoodadFuncRatioRespawn`. The target content table
`doodad_func_livestock_growths` is empty, so this work does not add an unused specialized livestock function class.
Farmhand crop and livestock jobs and farming-slot expansion are supported; Farmhand specialty trade is outside the
current scope.

Farmhand crop and livestock jobs continue while their owner is offline. The server stores the job's static
`butler_harvests` row, requested amount, remaining repeat count, registration Labor cost, and its last cycle
timestamp. A periodic worker advances due jobs from that persisted timestamp, with a bounded number of overdue
cycles per scan. This keeps long offline catch-up work from monopolizing one server tick.

Each completed interval rolls the harvest's base loot pack once per requested unit. Successful bonus checks roll
the bonus pack for that unit. These pack rolls use the content group's ten-million-scale probability, integer item
weights, and inclusive amount range directly; character, proficiency, quest, World loot-rate, and gold-rate
modifiers do not apply. Rewards are split by the item template's normal stack limit and sent in groups of at most
ten attachments. The mail wire uses native `MAIL_FROM_BUTLER` type 49, sender `.butlerHarvest`, title `title`, and
body `body(itemType, requestedAmount, withBonus)`.

The completion marker, updated or removed job, generated item rows, reward mail, and final Farmhand XP are written
in one MySQL transaction. Live job state, client packets, and mailbox publication happen only after commit. A
cancelled job grants no XP. Registration Labor is awarded as Farmhand XP once, when the final repeat completes,
so a repeating harvest does not multiply the original registration cost.

## Database

Apply `SQL/updates/2026-09-12_aaemu_game_character_butlers.sql` first, then
`SQL/updates/2026-09-12_aaemu_game_farmhand_farming.sql`. The farming migration extends the base
`character_butlers` record and adds Farmhand permanent data, harvest jobs, completion markers, and durable stored
item references.

## Configuration

`AAEmu.Game/Configurations/Butler.json` contains the server-owned completion policy:

- `ScanIntervalSeconds` controls how often persisted jobs are checked.
- `MaxCatchUpCyclesPerPass` limits overdue intervals processed for one job during one scan.
- `BonusRatioScale` is the denominator for `butler_harvests.bonus_ratio`. The default `10000` interprets the
  content value as basis points. Set it to `0` to disable bonus harvests.
- `ExperienceRate` multiplies formula 19, `exp_by_labor_power`, using the Farmhand's current level as `pc_level`
  and the job's stored registration Labor as `labor_power`. Set it to `0` to disable harvest XP.

The original World backend is unavailable, and the client does not evaluate either bonus probability or harvest
XP. The default ratio scale and use of formula 19 are documented emulator policies. They do not claim to reproduce
an unpublished retail formula. Current Farmhand unit-attribute payloads are empty, so growth-time attribute 228 and
bonus-ratio attribute 229 contribute zero until Farmhand equipment attributes are implemented.

Farmhand renaming follows the target client's `summons` name policy and its stricter 25-character Farmhand input
limit. The server currently accepts the conservative `en_us` subset of 2–25 ASCII letters or digits and preserves
the submitted case. Names may use at most 128 UTF-8 bytes. The client also checks a locale profanity dictionary,
but that dictionary is not present in the available game content and AAEmu has no shared blocked-name service.

The server uses level 40 as the usable Farmhand cap documented by the
[official November 17, 2022 ArcheAge update](https://store.steampowered.com/news/posts/?appids=304030&enddate=1668616064&feed=steam_community_announcements).
The client clamps the displayed level against a cap supplied by the server; it does not embed 40 as a fixed client
constant. Content level 41 supplies the next XP threshold as a sentinel. Cumulative XP remains stored above that
threshold while level lookup continues to resolve level 40, preserving future XP without exposing the sentinel row
as a usable level.
