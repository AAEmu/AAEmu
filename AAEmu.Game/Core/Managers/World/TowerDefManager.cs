using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Runs vanilla TowerDef event definitions (tower_defs SQL table) end-to-end.
/// Drives Halcyona War (id=18), Crimson Rift (3/5/6), Grimghast Rift (13/15), Hasla Rift (12)
/// when something calls <see cref="Start"/> — either ZoneConflict on War-state entry, the
/// /towerdef GM command, or a future daily scheduler. Owns the per-prog wave progression,
/// kill tracking and broadcast.
/// </summary>
public class TowerDefManager : Singleton<TowerDefManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, TowerDefRunner> _activeRunners = new();
    private readonly object _lock = new();
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
            return;

        // Subscribe to OnUnitKilled in every world so kill-target progressions
        // work no matter where the wave NPCs are tagged.
        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            if (world?.Events == null)
                continue;
            world.Events.OnUnitKilled += OnUnitKilled;
        }

        // Periodic tick @ 1s. ~10 active events globally → cost is negligible.
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromSeconds(1), true);

        _initialized = true;
        Logger.Info("TowerDefManager initialized");
    }

    public bool IsRunning(uint towerDefId)
    {
        lock (_lock)
            return _activeRunners.ContainsKey(towerDefId);
    }

    /// <summary>
    /// Boots a TowerDef. Returns false if the id is unknown or already running.
    /// </summary>
    public bool Start(uint towerDefId, ushort zoneGroupId, uint eventZoneId)
    {
        TowerDefRunner runner;
        lock (_lock)
        {
            if (_activeRunners.ContainsKey(towerDefId))
            {
                Logger.Debug($"Start({towerDefId}): already running");
                return false;
            }

            var def = TowerDefGameData.Instance.GetTowerDef(towerDefId);
            if (def == null)
            {
                Logger.Warn($"Start({towerDefId}): no TowerDef row in tower_defs");
                return false;
            }
            // Allow definitions with zero progs (e.g. tower_def 19/20 victory followups carry only
            // TargetNpcSpawnId+force_end_time). Without progs the runner just waits for ForceEnd.
            var hasProgs = def.Progs != null && def.Progs.Count > 0;

            var forceEnd = def.ForceEndTime > 0f ? def.ForceEndTime : 4800f;
            // Victory follow-up tower_defs (19 Nuia, 20 Harani) have a single trivial prog whose
            // purpose is just to wrap the 1-hour celebration window. Auto-advancing it broadcasts
            // SCTowerDefWaveStartPacket which immediately overrides the Victory banner+message on
            // the client. Suppress AdvanceProg for those — Start sends the banner, ForceEnd
            // closes the event, and the Victory Envoy stays put for the full ForceEndTime.
            var isVictoryFollowup = towerDefId == NuiaVictoryTowerDefId
                                 || towerDefId == HarihiraVictoryTowerDefId;
            runner = new TowerDefRunner
            {
                Def = def,
                ZoneGroupId = zoneGroupId,
                EventZoneId = eventZoneId,
                CurrentProgIndex = -1,
                StartTime = DateTime.UtcNow,
                ForceEndTime = DateTime.UtcNow.AddSeconds(forceEnd),
                // First wave fires after FirstWaveAfter seconds; before that the start
                // packet is on screen and players can rally. No progs OR victory followup
                // → NextProgTime = MaxValue, so AdvanceProg never runs and the runner only
                // ends on ForceEnd (preserves the on-screen Victory banner full 1 hour).
                NextProgTime = (hasProgs && !isVictoryFollowup)
                    ? DateTime.UtcNow.AddSeconds(def.FirstWaveAfter)
                    : DateTime.MaxValue
            };
            _activeRunners.Add(towerDefId, runner);
        }

        BroadcastStart(runner);
        Logger.Info($"TowerDef {towerDefId} started (zoneGroup={zoneGroupId}, first wave in {runner.Def.FirstWaveAfter}s, force-end {runner.ForceEndTime:HH:mm:ss})");

        // Consume TargetNpcSpawnId: the tower_defs row's own "anchor" spawner (e.g. Halcyona's
        // controller NPC, or tower_def 19/20's victory envoy). Stored in
        // SpawnedByProgSpawnTargetId under sentinel key 0 so DespawnAll cleans it on Stop.
        if (runner.Def.TargetNpcSpawnId != 0)
        {
            try
            {
                var anchorList = SpawnAnchorSpawner(runner.Def.TargetNpcSpawnId);
                if (anchorList.Count > 0)
                    runner.SpawnedByProgSpawnTargetId[0u] = anchorList;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"TowerDef {towerDefId}: TargetNpcSpawnId {runner.Def.TargetNpcSpawnId} spawn failed");
            }
        }

        // Halcyona War camp-guard spawning moved to AdvanceProg() when prog 104 fires (after the
        // FirstWaveAfter announce delay = 300s) so retail behaviour ("erste 5 min keine NPCs, NPCs
        // erst nach Announce") is preserved. The Start() call still announces via SCTowerDefStart
        // and spawns the (invisible hellgate-model) War Anchor via TargetNpcSpawnId above.

        return true;
    }

    /// <summary>
    /// Ends a running TowerDef. Cleans up active spawners, broadcasts end packet, and if this
    /// was Halcyona War (tower_def 18) with a captured winner relic, chains the corresponding
    /// follow-up tower_def (19 = Nuia wins, 20 = Harani wins) per vanilla SQL semantics.
    /// </summary>
    public bool Stop(uint towerDefId)
    {
        TowerDefRunner runner;
        lock (_lock)
        {
            if (!_activeRunners.Remove(towerDefId, out runner))
                return false;
            // Despawn under the lock so a concurrent Tick/AdvanceProg can't mutate
            // SpawnedByProgSpawnTargetId while we iterate it.
            DespawnAll(runner);
        }

        BroadcastEnd(runner);
        Logger.Info($"TowerDef {towerDefId} stopped");

        // Halcyona War winner chain. tower_def 19 has kill_npc_id=13661 (Harani relic),
        // tower_def 20 has kill_npc_id=13647 (Nuia relic) — i.e. "the kill that triggers this
        // victory event is the death of the LOSER's relic". So when the Nuia relic (13647)
        // died, Harani won → run tower_def 20. When the Harani relic (13661) died, Nuia won →
        // run tower_def 19.
        if (runner.Def.Id == HalcyonaWarTowerDefId && runner.WinnerRelicTemplateId != 0)
        {
            var followup = runner.WinnerRelicTemplateId == HaraniRelicTemplateId
                ? NuiaVictoryTowerDefId
                : HarihiraVictoryTowerDefId;
            Logger.Info($"Halcyona War: relic {runner.WinnerRelicTemplateId} fell → starting follow-up tower_def {followup}");
            try
            {
                Start(followup, runner.ZoneGroupId, runner.EventZoneId);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Halcyona War: follow-up tower_def {followup} failed to start");
            }
        }
        return true;
    }

    // Halcyona War specifics. Encoded as constants because the relic NPCs and victory tower_defs
    // exist nowhere else in the data — generalizing this would mean a new abstraction (out of scope).
    private const uint HalcyonaWarTowerDefId = 18u;
    private const uint NuiaVictoryTowerDefId = 19u;
    private const uint HarihiraVictoryTowerDefId = 20u;
    private const uint NuiaRelicTemplateId = 13647u;   // killing this → Harani wins → run def 20
    private const uint HaraniRelicTemplateId = 13661u; // killing this → Nuia wins → run def 19
    private const uint HalcyonaRelicProgId = 105u;     // tower_def 18 prog that needs relics in world
    private const uint HalcyonaDefenseFlagProgId = 104u; // tower_def 18 first prog (Defense Flag) — camp guards spawn here after FirstWaveAfter delay
    private const uint NuiaRelicSpawnerId = 15200u;    // npc_spawner that holds NPC 13647
    private const uint HaraniRelicSpawnerId = 15214u;  // npc_spawner that holds NPC 13661

    // Halcyona War Golems — 5min Immobilize → auto-Mobilize → walk path,
    // 10min hard-respawn on death (skipping the 5min phase).
    private const uint NuiaGolemTemplateId = 13796u;
    private const uint HarihiraGolemTemplateId = 13798u;
    internal const uint NuiaGolemSpawnerId = 15355u;
    internal const uint HarihiraGolemSpawnerId = 15357u;
    private const uint GolemImmobilizeBuffId = 6772u;  // "정지 상태" — stun=t + root=t
    // Mobilizing buff per camp. 6784 → ai_command_set 322 → "nuia_golem_move" (W→E).
    // 6785 → ai_command_set 323 → "harihara_golem_move" (E→W, reverse of the Nuia path).
    // Applying the wrong one makes both golems walk the same direction and pass each other.
    private const uint NuiaGolemMobilizingBuffId = 6784u;
    private const uint HarihiraGolemMobilizingBuffId = 6785u;
    private const int GolemImmobilizeDurationMs = 5 * 60 * 1000;
    private static readonly TimeSpan GolemRespawnDelay = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Camp guards + extras spawned for the whole Halcyona War. The broken skill chain on the
    /// Defense Flag (13675) would normally spawn these, but the referenced 123124..123715 spawner
    /// rows are missing from this DB — we drive them directly. Each spawner's instance count
    /// (1..4) is controlled by the matching npc_spawns_towerdef_halcyona.json entries.
    /// </summary>
    private static readonly uint[] HalcyonaCampGuardSpawnerIds =
    {
        // Nuia camp (X≈9000, Y≈10015, Z≈183)
        15201u, // 13648 Auto Cannon (×3 positions)
        15202u, // 13649 Supply Officer
        15203u, // 13650 Defense Captain
        15204u, // 13651 Defender (×4)
        15355u, // 13796 Burst Doll
        // Center / victory-area NPC
        15206u, // 13653 Ryan (war veteran)
        // Harani camp (X≈9885, Y≈10585, Z≈209)
        15215u, // 13662 Auto Cannon (×3)
        15216u, // 13663 Supply Officer
        15217u, // 13664 Defense Captain
        15218u, // 13665 Defender (×4)
        15357u, // 13798 Magic Knight
    };

    /// <summary>
    /// Spawn an event spawner (identified by npc_spawners.id) across every loaded world. Used for
    /// tower_defs.target_npc_spawner_id anchors AND for the Halcyona relic fallback hook. Logs
    /// pre/post-DoSpawn counts so HalcyonaWar.log has the full diagnostic without grepping
    /// NpcSpawner debug noise.
    /// </summary>
    private static List<NpcSpawner> SpawnAnchorSpawner(uint spawnerId)
    {
        var spawned = new List<NpcSpawner>();
        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            var spawners = world?.SpawnManager?.GetNpcSpawner(spawnerId);
            if (spawners == null || spawners.Count == 0)
                continue;
            Logger.Debug($"[Spawn] spawner {spawnerId}: {spawners.Count} instance(s) in world {world.Id}");
            foreach (var sp in spawners)
            {
                try
                {
                    var beforeCount = sp.SpawnedNpcs.TryGetValue(sp.SpawnerId, out var beforeList)
                        ? beforeList.Count
                        : 0;
                    sp.DoSpawn();
                    var afterCount = sp.SpawnedNpcs.TryGetValue(sp.SpawnerId, out var afterList)
                        ? afterList.Count
                        : 0;
                    var newCount = afterCount - beforeCount;
                    if (newCount > 0)
                    {
                        var last = afterList[^1];
                        var pos = last?.Transform?.World?.Position ?? System.Numerics.Vector3.Zero;
                        var regionId = last?.Region?.Id;
                        var instanceId = last?.Transform?.InstanceId;
                        Logger.Info($"[Spawn] spawner {spawnerId} → +{newCount} NPC(s) (UnitId={sp.UnitId} ObjId={last?.ObjId} pos=({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) inst={instanceId} regionId={(regionId.HasValue ? regionId.Value.ToString() : "NULL")} visible={last?.IsVisible})");
                    }
                    else
                    {
                        Logger.Warn($"[Spawn] spawner {spawnerId}: DoSpawn produced 0 new NPCs (silent early-return — see Server.log for details; usually max-pop reached, SpawnableNpcs empty, or InitializeSpawnableNpcs not called)");
                    }
                    spawned.Add(sp);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[Spawn] DoSpawn threw for spawner {spawnerId}");
                }
            }
        }
        if (spawned.Count == 0)
            Logger.Warn($"[Spawn] spawner {spawnerId}: NOT REGISTERED as event spawner (missing npc_spawns_*.json entry with NpcSpawnerIds=[{spawnerId}])");
        return spawned;
    }

    private void Tick(TimeSpan delta)
    {
        if (!_initialized)
            return;

        List<TowerDefRunner> snapshot;
        lock (_lock)
            snapshot = _activeRunners.Values.ToList();

        var now = DateTime.UtcNow;
        foreach (var r in snapshot)
        {
            try
            {
                if (now >= r.ForceEndTime)
                {
                    Logger.Info($"TowerDef {r.Def.Id} force-end timer elapsed");
                    Stop(r.Def.Id);
                    continue;
                }

                if (now >= r.NextProgTime)
                {
                    AdvanceProg(r);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"TowerDef {r.Def.Id} tick failed");
            }
        }
    }

    private void AdvanceProg(TowerDefRunner r)
    {
        var nextIndex = r.CurrentProgIndex + 1;
        if (nextIndex >= r.Def.Progs.Count)
        {
            // Past the last prog → event finished successfully.
            Stop(r.Def.Id);
            return;
        }

        // Despawn whatever the previous prog flagged as despawn_on_next_step.
        if (r.CurrentProgIndex >= 0)
        {
            var prev = r.Def.Progs[r.CurrentProgIndex];
            foreach (var st in prev.SpawnTargets)
            {
                if (!st.DespawnOnNextStep)
                    continue;
                if (r.SpawnedByProgSpawnTargetId.TryGetValue(st.Id, out var spawnedList))
                {
                    foreach (var spawner in spawnedList)
                    {
                        try
                        {
                            spawner?.DespawnNpcsNow();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Despawn failed for spawner {spawner?.Id}");
                        }
                    }
                    r.SpawnedByProgSpawnTargetId.Remove(st.Id);
                }
            }
        }

        r.CurrentProgIndex = nextIndex;
        var prog = r.Def.Progs[nextIndex];

        // Spawn the new prog's NpcSpawner targets — reuse SpawnAnchorSpawner so the per-spawner
        // diagnostic (pre/post count + ObjId/position) lands in HalcyonaWar.log.
        foreach (var st in prog.SpawnTargets)
        {
            if (!string.Equals(st.SpawnTargetType, "NpcSpawner", StringComparison.OrdinalIgnoreCase))
                continue;

            var spawnedList = SpawnAnchorSpawner(st.SpawnTargetId);
            if (spawnedList.Count > 0)
                r.SpawnedByProgSpawnTargetId[st.Id] = spawnedList;
        }

        // Reset kill counters; only the active prog's targets matter.
        r.KillsByTemplateId.Clear();
        foreach (var kt in prog.KillTargets)
            r.KillsByTemplateId[kt.KillTargetId] = 0;

        // Timer for "auto-advance" — 0 means "advance only on kill condition".
        r.NextProgTime = prog.CondToNextTime > 0f
            ? DateTime.UtcNow.AddSeconds(prog.CondToNextTime)
            : DateTime.MaxValue;

        BroadcastWaveStart(r, (uint)nextIndex);
        Logger.Info($"TowerDef {r.Def.Id} prog #{nextIndex} (id={prog.Id}) — {prog.SpawnTargets.Count} spawn / {prog.KillTargets.Count} kill targets; auto={(prog.CondToNextTime > 0 ? prog.CondToNextTime + "s" : "kill-only")}");

        // Halcyona War: prog 104 is the Defense Flag phase, fired ~5 minutes after the
        // Conflict→War announce (tower_defs.first_wave_after = 300s). Retail behaviour: NPCs
        // do NOT appear until the Defense Flag spawns. Spawn the camp guards + extras here
        // so they appear in sync with the visible "phase" change instead of immediately at
        // war start (which would break retail timing — Will: "ersten 5 min keine NPCs").
        if (r.Def.Id == HalcyonaWarTowerDefId && prog.Id == HalcyonaDefenseFlagProgId)
        {
            foreach (var guardSpawnerId in HalcyonaCampGuardSpawnerIds)
            {
                try
                {
                    var spawned = SpawnAnchorSpawner(guardSpawnerId);
                    if (spawned.Count > 0)
                    {
                        r.SpawnedByProgSpawnTargetId[uint.MaxValue - guardSpawnerId] = spawned;
                        // Golem spawners need extra wiring: 5min Immobilize timer that auto-
                        // flips into Mobilized + FollowPath, and an OnDeath handler that
                        // schedules a 10min skip-immobilize respawn.
                        if (guardSpawnerId == NuiaGolemSpawnerId || guardSpawnerId == HarihiraGolemSpawnerId)
                            WireFreshlySpawnedGolems(spawned, skipImmobilize: false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Halcyona War: guard spawn fallback failed for spawner {guardSpawnerId}");
                }
            }
        }

        // Halcyona War: prog 105 is the relic-spawn phase. In retail the Battle Marker NPC
        // (13678) casts a skill that fires NpcSpawnerSpawnEffect to spawn the two relics — but
        // in this vanilla DB the skill's target SpawnerIds (123122/123132) don't exist in
        // npc_spawners, so the relic chain is silently broken. Fallback: spawn the relic
        // spawners 15200 (Nuia) + 15214 (Harani) directly here so prog 105's kill targets
        // actually exist. Tracked in SpawnedByProgSpawnTargetId under synthetic keys derived
        // from spawner_id so Stop()/DespawnAll cleans them up.
        if (r.Def.Id == HalcyonaWarTowerDefId && prog.Id == HalcyonaRelicProgId)
        {
            foreach (var relicSpawnerId in new uint[] { NuiaRelicSpawnerId, HaraniRelicSpawnerId })
            {
                try
                {
                    var spawned = SpawnAnchorSpawner(relicSpawnerId);
                    if (spawned.Count > 0)
                    {
                        // Synthetic key in the high-range so it can't collide with real
                        // tower_def_prog_spawn_target.id values (those are well under 1000).
                        r.SpawnedByProgSpawnTargetId[uint.MaxValue - relicSpawnerId] = spawned;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Halcyona War: relic spawn fallback failed for spawner {relicSpawnerId}");
                }
            }
        }
    }

    private void OnUnitKilled(object sender, OnUnitKilledArgs e)
    {
        if (e.Victim is not Npc npc)
            return;
        var killedTemplateId = npc.TemplateId;
        if (killedTemplateId == 0)
            return;

        List<TowerDefRunner> snapshot;
        lock (_lock)
            snapshot = _activeRunners.Values.ToList();

        foreach (var r in snapshot)
        {
            if (r.CurrentProgIndex < 0 || r.CurrentProgIndex >= r.Def.Progs.Count)
                continue;
            var prog = r.Def.Progs[r.CurrentProgIndex];

            var matched = false;
            foreach (var kt in prog.KillTargets)
            {
                if (!string.Equals(kt.KillTargetType, "Npc", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (kt.KillTargetId != killedTemplateId)
                    continue;
                r.KillsByTemplateId.TryGetValue(kt.KillTargetId, out var soFar);
                r.KillsByTemplateId[kt.KillTargetId] = soFar + 1;
                matched = true;

                // Halcyona War: remember WHICH relic fell so Stop() can chain to the right
                // victory follow-up. This must be set before KillsByTemplateId is cleared on
                // the next AdvanceProg.
                if (r.Def.Id == HalcyonaWarTowerDefId &&
                    (killedTemplateId == NuiaRelicTemplateId || killedTemplateId == HaraniRelicTemplateId))
                {
                    r.WinnerRelicTemplateId = killedTemplateId;
                }
            }
            if (!matched)
                continue;

            if (IsKillConditionSatisfied(prog, r))
            {
                // Fast-track: next tick advances. We don't advance inline here to keep
                // mutation out of the kill event hot path.
                r.NextProgTime = DateTime.UtcNow;
                Logger.Info($"TowerDef {r.Def.Id} prog #{r.CurrentProgIndex} kill condition satisfied (npc {killedTemplateId})");
            }
        }
    }

    private static bool IsKillConditionSatisfied(TowerDefProg prog, TowerDefRunner r)
    {
        if (prog.KillTargets.Count == 0)
            return false;

        var allOk = true;
        var anyOk = false;
        foreach (var kt in prog.KillTargets)
        {
            r.KillsByTemplateId.TryGetValue(kt.KillTargetId, out var got);
            var ok = got >= kt.KillCount;
            if (ok) anyOk = true;
            else allOk = false;
        }
        return prog.CondCompByAnd ? allOk : anyOk;
    }

    /// <summary>
    /// Walks a list of NpcSpawners that just had <see cref="NpcSpawner.DoSpawn"/> called for
    /// the two Halcyona War Golem spawners (15355 Nuia / 15357 Harihara), finds the freshly-
    /// spawned Golem instance(s), and arms the Immobilize→Mobilize→Respawn lifecycle.
    /// </summary>
    /// <param name="spawners">NpcSpawner instances returned by <see cref="SpawnAnchorSpawner"/>.</param>
    /// <param name="skipImmobilize">true → respawn path (apply Mobilized immediately, no Immobilize phase).</param>
    private void WireFreshlySpawnedGolems(IEnumerable<NpcSpawner> spawners, bool skipImmobilize)
    {
        foreach (var sp in spawners)
        {
            if (sp == null) continue;
            if (!sp.SpawnedNpcs.TryGetValue(sp.SpawnerId, out var list) || list.Count == 0)
                continue;
            // The newly spawned golem is the last entry — DoSpawn appended it.
            var npc = list[^1];
            if (npc == null) continue;
            if (npc.TemplateId != NuiaGolemTemplateId && npc.TemplateId != HarihiraGolemTemplateId)
                continue;

            // Disable the engine-driven respawn poll so it doesn't race our 10min TaskManager
            // pipeline (would otherwise re-spawn alongside the skip-immobilize path).
            if (npc.Spawner != null)
                npc.Spawner.RespawnTime = 0;

            AttachGolemBehaviour(npc, skipImmobilize, sp.SpawnerId);
        }
    }

    /// <summary>
    /// Per-instance lifecycle wiring for a Halcyona War Golem. Applies the 5-min Immobilize
    /// buff (forced duration — DB row has duration=0), subscribes a one-shot OnTimeout that
    /// flips to Mobilized when the immobilize naturally expires (or is dispelled by Motor
    /// activation — RemoveBuff also routes through StopEffectTask which fires OnTimeout),
    /// and subscribes a one-shot OnDeath that schedules the 10-min respawn.
    /// </summary>
    private void AttachGolemBehaviour(Npc npc, bool skipImmobilize, uint spawnerId)
    {
        if (skipImmobilize)
        {
            Logger.Info($"[Halcyona Golem] respawn ObjId={npc.ObjId} template={npc.TemplateId} spawner={spawnerId} — skipping Immobilize, applying Mobilized immediately");
            // The OnSpawn np_skills hook (skill 23507) would have applied 6772 with permanent
            // duration. We disable that hook in NpcEvents for these two templates, but as a
            // belt-and-braces guard, also strip it here in case anything else added it.
            try { npc.Buffs?.RemoveBuff(GolemImmobilizeBuffId); } catch { /* best-effort */ }
            ApplyMobilized(npc);
        }
        else
        {
            try
            {
                var immobTemplate = SkillManager.Instance.GetBuffTemplate(GolemImmobilizeBuffId);
                if (immobTemplate == null)
                {
                    Logger.Warn($"[Halcyona Golem] buff template {GolemImmobilizeBuffId} not found — cannot arm Immobilize timer");
                }
                else
                {
                    var caster = new SkillCasterUnit(npc.ObjId);
                    var buff = new Buff(npc, npc, caster, immobTemplate, null, DateTime.UtcNow);
                    npc.Buffs.AddBuff(buff, 0, forcedDuration: GolemImmobilizeDurationMs);

                    // One-shot OnTimeout: covers natural 5-min expiry AND Motor-item dispel
                    // (RemoveBuff → Buff.Exit → StopEffectTask → OnTimeout fires either way).
                    EventHandler<OnTimeoutArgs> onTimeout = null;
                    onTimeout = (s, a) =>
                    {
                        buff.Events.OnTimeout -= onTimeout;
                        ApplyMobilized(npc);
                    };
                    buff.Events.OnTimeout += onTimeout;
                    Logger.Info($"[Halcyona Golem] spawn ObjId={npc.ObjId} template={npc.TemplateId} spawner={spawnerId} — Immobilize armed (mobilize at {DateTime.UtcNow.AddMilliseconds(GolemImmobilizeDurationMs):HH:mm:ss})");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Halcyona Golem] failed to arm Immobilize for ObjId={npc.ObjId}");
            }
        }

        // One-shot OnDeath: schedule the 10-min respawn job for this spawner.
        // Captures spawnerId only — npc reference goes out of scope after Despawn.
        EventHandler<OnDeathArgs> onDeath = null;
        onDeath = (s, a) =>
        {
            npc.Events.OnDeath -= onDeath;
            try
            {
                var task = new HalcyonaGolemRespawnTask(spawnerId);
                TaskManager.Instance.Schedule(task, GolemRespawnDelay);
                Logger.Info($"[Halcyona Golem] died ObjId={npc.ObjId} template={npc.TemplateId} spawner={spawnerId} — respawn in {GolemRespawnDelay.TotalSeconds:F0}s");
                // NOTE: TaskManager state is in-memory — a server restart inside the 10min
                // respawn window will lose this timer; the golem will only re-appear with the
                // next Halcyona War event.
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Halcyona Golem] failed to schedule respawn for spawner {spawnerId}");
            }
        };
        npc.Events.OnDeath += onDeath;
    }

    /// <summary>
    /// Applies the side-correct Mobilizing buff to a golem. The buff's Started trigger
    /// (buff_triggers id 4187/4188) routes through NpcControlEffect → RunCommandSet which
    /// reads the FollowPath filename from ai_commands. Picking the wrong side's buff = wrong
    /// path = both golems walking in the same direction. Guarded against double-apply and
    /// corpse-cast.
    /// </summary>
    private static void ApplyMobilized(Npc npc)
    {
        if (npc == null || npc.IsDead || npc.Region == null) return;
        if (npc.Buffs == null) return;
        var mobilizingBuffId = npc.TemplateId == HarihiraGolemTemplateId
            ? HarihiraGolemMobilizingBuffId
            : NuiaGolemMobilizingBuffId;
        if (npc.Buffs.CheckBuff(mobilizingBuffId)) return;
        try
        {
            npc.Buffs.AddBuff(mobilizingBuffId, npc);
            Logger.Info($"[Halcyona Golem] mobilized ObjId={npc.ObjId} template={npc.TemplateId} — FollowPath chain armed via buff {mobilizingBuffId}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"[Halcyona Golem] AddBuff({mobilizingBuffId}) threw for ObjId={npc.ObjId}");
        }
    }

    /// <summary>
    /// Public entry point invoked by <see cref="HalcyonaGolemRespawnTask"/> ~10 min after a
    /// golem dies. Re-runs the camp guard spawner and wires the new instance with
    /// skipImmobilize=true so it walks the path immediately.
    /// </summary>
    public void RespawnHalcyonaGolem(uint spawnerId)
    {
        if (spawnerId != NuiaGolemSpawnerId && spawnerId != HarihiraGolemSpawnerId)
        {
            Logger.Warn($"[Halcyona Golem] RespawnHalcyonaGolem called with non-golem spawner {spawnerId}");
            return;
        }
        try
        {
            var spawned = SpawnAnchorSpawner(spawnerId);
            if (spawned.Count == 0)
            {
                Logger.Warn($"[Halcyona Golem] respawn for spawner {spawnerId} produced 0 NPCs — skipping wire");
                return;
            }
            WireFreshlySpawnedGolems(spawned, skipImmobilize: true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"[Halcyona Golem] respawn pipeline threw for spawner {spawnerId}");
        }
    }

    private static void DespawnAll(TowerDefRunner r)
    {
        foreach (var (_, list) in r.SpawnedByProgSpawnTargetId)
        {
            foreach (var spawner in list)
            {
                try
                {
                    spawner?.DespawnNpcsNow();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"DespawnAll: failed for spawner {spawner?.Id}");
                }
            }
        }
        r.SpawnedByProgSpawnTargetId.Clear();
    }

    private static void BroadcastStart(TowerDefRunner r)
    {
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefStartPacket(
            new TowerDefKey { TowerDefId = r.Def.Id, ZoneGroupId = r.ZoneGroupId },
            r.EventZoneId));
    }

    private static void BroadcastEnd(TowerDefRunner r)
    {
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefEndPacket(
            new TowerDefKey { TowerDefId = r.Def.Id, ZoneGroupId = r.ZoneGroupId },
            r.EventZoneId));
    }

    private static void BroadcastWaveStart(TowerDefRunner r, uint step)
    {
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefWaveStartPacket(
            new TowerDefKey { TowerDefId = r.Def.Id, ZoneGroupId = r.ZoneGroupId },
            r.EventZoneId, step));
    }
}

/// <summary>
/// Per-event runtime state. Lives in <see cref="TowerDefManager._activeRunners"/> until the
/// event ends. All mutation goes through the manager under its lock.
/// </summary>
internal sealed class TowerDefRunner
{
    public TowerDef Def;
    public ushort ZoneGroupId;
    public uint EventZoneId;

    /// <summary>-1 before the first prog has been advanced into.</summary>
    public int CurrentProgIndex;

    public DateTime StartTime;
    public DateTime ForceEndTime;

    /// <summary>Wall-clock at which Tick() advances to the next prog (auto-advance path).
    /// MaxValue means "kill-condition only — no timer".</summary>
    public DateTime NextProgTime = DateTime.MaxValue;

    /// <summary>NPC template id → kill count accumulated for the CURRENT prog. Reset on advance.</summary>
    public readonly Dictionary<uint, uint> KillsByTemplateId = new();

    /// <summary>spawn_target.id → spawners that have been DoSpawn()'d for it. Used to despawn
    /// when the next prog steps over a despawn_on_next_step row, and at Stop() for the lot.
    /// Sentinel key 0 holds the tower_defs.target_npc_spawner_id anchor (e.g. victory envoy).</summary>
    public readonly Dictionary<uint, List<NpcSpawner>> SpawnedByProgSpawnTargetId = new();

    /// <summary>Halcyona War: the relic NPC template id that died and ended prog 105.
    /// 13647 (Nuia) → Harani wins; 13661 (Harani) → Nuia wins. 0 = none yet / not applicable.</summary>
    public uint WinnerRelicTemplateId;
}
