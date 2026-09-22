# Original dungeon mentoring restoration

ArcheAge 4.0 removed the accept-source bindings for part of the original low-level dungeon mentoring
system while retaining its quest contexts, role-seal objectives, chest skills, requirements, and rewards.
AAEmu restores the eight retained quests when the client confirms entry into their authored dungeon.

The restored set is quest contexts 6083/6084 (Okape), 6087/6088 (Hieronimus), 6166/6167 (Akmit),
and 6168/6169 (Marmas). Their content zone ids (resolved from the runtime zone key), minimum levels,
mentee maximum-level requirements, objectives, auto-completion, and rewards are read from the configured
decrypted game-content database. Hieronimus and Marmas retain their authored NPC acceptors (14422 and
14423); the two earlier pairs have empty Start components and are started by the server after successful
dungeon entry instead of inventing an NPC link.

The available instance spawn assets omit three of the four mentoring chests. For those dungeons, AAEmu
derives the defeated-boss trigger from the quest chest objective and the dungeon action or on-death skill
graph, then creates the missing chest at that boss's authoritative transform. It derives the mentor opening
phase from the same authored interaction data, reuses an existing chest when one is already present, and
prevents duplicate spawns.

These quests use `detail_id = 7`. Existing daily quest handling clears their completion bits at 00:00 UTC,
including a missed reset detected at login. Active quests and quests already completed that day are not
started again on dungeon re-entry or reconnect. The decrypted content database remains read-only and is
not patched by this restoration.
