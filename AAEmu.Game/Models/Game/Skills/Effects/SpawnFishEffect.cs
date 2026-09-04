using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnFishEffect : EffectTemplate
{
    public uint Range { get; set; }
    public uint DoodadId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character player) return;

        if (SportFishCombat.HasActiveHook(player.Id))
        {
            Logger.Info("Skipped SpawnFish for {0}: a hooked fish is still on the line", player.Name);
            return;
        }

        var fishSpawnerId = GetFishSpawnerId(player, target);
        if (fishSpawnerId == 0)
        {
            Logger.Debug($"Fish Spawner ID not found for player {player.Name}.");
            return;
        }

        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(fishSpawnerId);
        if (template?.Npcs == null || template.Npcs.Count == 0)
        {
            Logger.Warn($"No NPC templates available for fish spawner {fishSpawnerId}.");
            return;
        }

        var npcTemplateEntry = FishSchoolLookup.SelectWeighted(template.Npcs, Random.Shared.NextDouble());

        if (npcTemplateEntry == null)
        {
            Logger.Warn($"Failed to select random fish for spawner {fishSpawnerId}");
            return;
        }

        Logger.Debug($"Selected fish template {npcTemplateEntry.MemberId} from spawner {fishSpawnerId}");

        if (WorldIntegration.ZoneAuthority)
        {
            SpawnFishInZone(player, target, npcTemplateEntry, castObj, source);
            return;
        }

        // Create temporary spawner with correct position
        var tempSpawner = new NpcSpawner
        {
            ParentWorld = player.ParentWorld,
            SpawnerId = fishSpawnerId,
            UnitId = npcTemplateEntry.MemberId,
            Template = template
        };

        // Fix for CloneAsSpawnPosition not existing / type mismatch
        using var spawnPos = target.Transform.Clone();
        tempSpawner.Position = spawnPos.CloneAsSpawnPosition();

        try
        {
            var spawnedList = npcTemplateEntry.Spawn(tempSpawner, player.Id);
            if (spawnedList == null || spawnedList.Count == 0)
            {
                Logger.Warn($"npcTemplate.Spawn returned no fish for template {npcTemplateEntry.MemberId}");
                return;
            }
            // Register so that Despawn() -> RemoveNpcFromSpawnedList doesn't log a false warning
            tempSpawner.SpawnedNpcs.TryAdd(fishSpawnerId, spawnedList);
            var fish = spawnedList.First();
            SportFishCombat.RegisterHook(player.Id, fish.ObjId);
            // Aggro & targeting
            fish.CurrentTarget = player;
            fish.AddUnitAggro(AggroKind.Damage, player, 10000);
            // The fish is spawned already hooked on the angler, so it enters combat here rather than
            // through the aggro sweep an ordinary NPC uses. Announcing it is what releases the
            // SkillUseConditionKind.InCombat skills every sport fish carries — 입질 (21608) applies
            // the tag 1090 buff that plot 821 waits on, and without it the plot times out seven
            // seconds later into 대어 소환 안됨 ("big fish not summoned") with the fish left floating.
            fish.Events.OnCombatStarted(fish, new OnCombatStartedArgs { Owner = fish, Target = player });
            player.CurrentTarget = fish;
            player.SendPacket(new SCTargetChangedPacket(player.ObjId, fish.ObjId));
            Logger.Debug($"Successfully spawned fish {npcTemplateEntry.MemberId} (owner {player.Id}) at bobber for {player.Name}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error spawning fish from template {npcTemplateEntry.MemberId}");
        }
    }

    private void SpawnFishInZone(
        Character player,
        BaseUnit target,
        NpcSpawnerNpc npcTemplateEntry,
        CastAction castAction,
        EffectSource effectSource)
    {
        Npc fish = null;
        var publishAttempted = false;
        var published = false;
        try
        {
            fish = NpcManager.Instance.Create(player.ParentWorld, 0, npcTemplateEntry.MemberId);
            if (fish == null)
            {
                Logger.Warn($"Fish template {npcTemplateEntry.MemberId} could not be created.");
                return;
            }

            fish.OwnerId = player.Id;
            fish.Transform = target.Transform.CloneDetached(fish);
            fish.IsZoneMirror = true;
            // NpcManager.Create does not register np_skills. The non-ZA path goes through
            // NpcSpawnerNpc.Spawn, which does — bite / OnDeath school-count need the same hook.
            fish.RegisterNpcEvents();
            fish.Spawn();
            SportFishCombat.RegisterHook(player.Id, fish.ObjId);

            void CompleteCombatHandoff()
            {
                // Native spawning.lua removes buff 815 when the NPC leaves its spawn state. The
                // hook plot event and that removal must both complete before combat is handed off;
                // otherwise spawning's destructor clears the target and combat relation again.
                if (fish.ParentWorld?.GetNpc(fish.ObjId) != fish)
                {
                    Logger.Debug("Skipped stale fish handoff obj={0} player={1}", fish.ObjId, player.Id);
                    return;
                }

                if (!player.IsOnline || player.ParentWorld != fish.ParentWorld)
                {
                    Logger.Debug("Retiring fish handoff obj={0}; player={1} is no longer present", fish.ObjId, player.Id);
                    WorldIntegration.DeleteNpcMirror(fish, true);
                    return;
                }

                try
                {
                    fish.CurrentTarget = player;
                    WorldIntegration.RelayTargetChangedToZone?.Invoke(fish.ObjId, player.ObjId, true);
                    player.CurrentTarget = fish;
                    player.SendPacket(new SCTargetChangedPacket(player.ObjId, fish.ObjId));
                    WorldIntegration.RelayTargetChangedToZone?.Invoke(player.ObjId, fish.ObjId, true);
                    WorldIntegration.PublishAggro(fish, player, 10000, castAction);
                    // Must run before IsInBattle is set: OnCombatStarted no-ops when already in battle.
                    // Under ZoneAuthority this only applies bite 21608 (plot 821 / tag 1090).
                    fish.Events.OnCombatStarted(fish, new OnCombatStartedArgs { Owner = fish, Target = player });
                    player.IsInBattle = true;
                    Logger.Debug(
                        $"Successfully handed fish {npcTemplateEntry.MemberId} (owner {player.Id}) to Zone at the bobber.");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed fish combat handoff obj={0}; retiring Zone mirror", fish.ObjId);
                    WorldIntegration.DeleteNpcMirror(fish, true);
                }
            }

            WorldIntegration.RegisterNpcHandoff(
                fish.ObjId,
                (uint)BuffConstants.ZoneNpcSpawnProtection,
                CompleteCombatHandoff);

            // Native NpcManager::Create resolves a non-empty creator identity to that creator's
            // owner/faction before constructing the NPC. Sending the angler here therefore makes
            // the fish inherit the angler's side and its hostile bite cannot target them. Retail
            // attributes this spawn with NpcSpawnReason::Fishing and its CastAction payload while
            // leaving the creator identity empty, so the fish keeps its template faction.
            publishAttempted = true;
            published = WorldIntegration.PublishNpcSpawn(
                fish,
                reason: NpcSpawnReasonType.Fishing,
                spawnAction: castAction);
            if (!published)
            {
                WorldIntegration.CancelNpcHandoff(fish.ObjId);
                WorldIntegration.DeleteNpcMirror(fish, false);
                return;
            }

            if (effectSource?.DeferUntilPlotEventProcessed(
                    () => WorldIntegration.MarkNpcHandoffPlotReady(fish.ObjId)) != true)
                WorldIntegration.MarkNpcHandoffPlotReady(fish.ObjId);
        }
        catch (Exception ex)
        {
            if (fish != null)
            {
                WorldIntegration.CancelNpcHandoff(fish.ObjId);
                WorldIntegration.DeleteNpcMirror(fish, publishAttempted);
            }

            Logger.Error(ex, $"Error handing fish template {npcTemplateEntry.MemberId} to Zone.");
        }
    }

    /// <summary>
    /// Finds the school the bobber landed in and returns the npc_spawners id its current phase feeds
    /// from. Only the chummed phase carries a <see cref="DoodadFuncFishSchool"/> — freshwater 6447
    /// holds it on 26363 and not on the idle 26362 — so an un-chummed school correctly yields 0.
    /// </summary>
    private uint GetFishSpawnerId(Character player, BaseUnit origin)
    {
        // spawn_fish_effects.range is millimetres, as in ScopedFEffect: 50000 -> 50m for the school
        // effects plot 821 and 809 use, 25000 -> 25m for effect 1.
        var searchOrigin = origin ?? player;
        if (searchOrigin == null)
            return 0;

        var rangeMeters = Range / 1000f;
        var doodads = WorldManager.GetAround<Doodad>(searchOrigin, rangeMeters);
        var schools = new List<(float X, float Y, uint SpawnerId)>(doodads.Count);
        foreach (var doodad in doodads)
        {
            var spawnerId = FishSchoolLookup.ReadActiveSpawnerId(
                doodad,
                DoodadManager.Instance.GetPhaseFuncTemplate);
            var pos = doodad.Transform.World.Position;
            schools.Add((pos.X, pos.Y, spawnerId));
        }

        var originPos = searchOrigin.Transform.World.Position;
        return FishSchoolLookup.ResolveNearestSpawnerId(schools, originPos.X, originPos.Y, rangeMeters);
    }
}
