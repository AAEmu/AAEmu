using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;

using NLog;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Restores mentoring chests whose original instance placements are absent from the server assets.
/// </summary>
public static class MentoringChestRestorationService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void PrepareForBossDeath(Npc npc)
    {
        try
        {
            var world = npc?.ParentWorld;
            var dungeon = world?.DungeonInstance;
            if (npc == null || world == null || dungeon == null)
                return;

            var zoneGroupId = dungeon.GetZoneGroupId;
            if (!MentoringChestGameData.Instance.TryGetTrigger(zoneGroupId, npc.TemplateId, out var trigger))
                return;

            var result = PrepareForBossDeathLocked(
                world,
                npc,
                true,
                zoneGroupId,
                trigger,
                doodadTemplateId => world.GetDoodadsByTemplateId(doodadTemplateId).Count > 0,
                selected => SpawnAtBoss(npc, selected));

            if (result == MentoringChestPreparationResult.Spawned)
            {
                Logger.Info(
                    "Restored mentoring chest doodad={0} phase={1} for npc={2} in zoneGroup={3}",
                    trigger.DoodadTemplateId,
                    trigger.ExposedPhaseId,
                    npc.TemplateId,
                    zoneGroupId);
            }
            else if (result == MentoringChestPreparationResult.SpawnFailed)
            {
                Logger.Error(
                    "Failed to restore mentoring chest doodad={0} for npc={1} in zoneGroup={2}",
                    trigger.DoodadTemplateId,
                    npc.TemplateId,
                    zoneGroupId);
            }
        }
        catch (Exception ex)
        {
            // A restoration failure must not interrupt the normal NPC death path.
            Logger.Error(ex, "Mentoring chest restoration failed for npc={0}", npc?.TemplateId ?? 0);
        }
    }

    internal static MentoringChestPreparationResult PrepareForBossDeath(
        Npc npc,
        bool isDungeonInstance,
        uint zoneGroupId,
        MentoringChestTrigger trigger,
        Func<uint, bool> chestExists,
        Func<MentoringChestTrigger, Doodad> spawnChest)
    {
        if (npc == null || !isDungeonInstance || trigger == null || chestExists == null || spawnChest == null)
            return MentoringChestPreparationResult.NotApplicable;
        if (zoneGroupId != trigger.ZoneGroupId || npc.TemplateId != trigger.NpcTemplateId)
            return MentoringChestPreparationResult.NotApplicable;
        if (!npc.TryConsumeMentoringChestPreparation())
            return MentoringChestPreparationResult.AlreadyPrepared;
        if (chestExists(trigger.DoodadTemplateId))
            return MentoringChestPreparationResult.ReusedExisting;

        return spawnChest(trigger) == null
            ? MentoringChestPreparationResult.SpawnFailed
            : MentoringChestPreparationResult.Spawned;
    }

    internal static MentoringChestPreparationResult PrepareForBossDeathLocked(
        object worldSync,
        Npc npc,
        bool isDungeonInstance,
        uint zoneGroupId,
        MentoringChestTrigger trigger,
        Func<uint, bool> chestExists,
        Func<MentoringChestTrigger, Doodad> spawnChest)
    {
        if (worldSync == null)
            return MentoringChestPreparationResult.NotApplicable;

        // Boss variants can die almost simultaneously in a copied instance. Serialize the
        // same-world existence check and creation so they cannot produce duplicate chests.
        lock (worldSync)
        {
            return PrepareForBossDeath(
                npc,
                isDungeonInstance,
                zoneGroupId,
                trigger,
                chestExists,
                spawnChest);
        }
    }

    private static Doodad SpawnAtBoss(Npc boss, MentoringChestTrigger trigger)
    {
        var world = boss.ParentWorld;
        if (world?.DungeonInstance == null)
            return null;

        var spawner = CreateSpawner(boss, trigger);
        var doodad = spawner.Spawn(0);
        if (doodad == null)
            return null;

        doodad.DoChangePhase(null, (int)trigger.ExposedPhaseId);
        if (doodad.FuncGroupId == trigger.ExposedPhaseId)
            return doodad;

        spawner.Despawn(doodad);
        return null;
    }

    internal static DoodadSpawner CreateSpawner(Npc boss, MentoringChestTrigger trigger)
    {
        var world = boss?.ParentWorld;
        if (world == null || trigger == null)
            return null;

        return new MentoringChestSpawner
        {
            ParentWorld = world,
            Id = 0,
            UnitId = trigger.DoodadTemplateId,
            FuncGroupId = trigger.InitialPhaseId,
            Position = boss.Transform.CloneAsSpawnPosition()
        };
    }

    /// <summary>
    /// Final-phase respawns retain their spawner. A dungeon can be disposed while its authored
    /// delay is pending, so a runtime placement must reject the late callback explicitly.
    /// </summary>
    private sealed class MentoringChestSpawner : DoodadSpawner
    {
        public override Doodad Spawn(uint objId)
        {
            return ParentWorld is { IsDisposed: false } ? base.Spawn(objId) : null;
        }
    }
}

internal enum MentoringChestPreparationResult
{
    NotApplicable,
    AlreadyPrepared,
    ReusedExisting,
    Spawned,
    SpawnFailed
}
