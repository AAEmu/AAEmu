using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// Boot check on the main world: every actor of the six race starter chains
/// (<see cref="StarterChainActorRules"/>) must either stand in the world as a doodad
/// (doodad_spawns.json or the level pack) or, for NPCs, have an npc_spawner_npcs row the birth
/// partition can arm. When ZoneGameDataRoot is set the birth partition's npc_spawners.g is checked
/// too; a chain NPC placed only in a sibling partition is reported, since hosting the birth
/// partition alone leaves it unspawned (quest-npc-partition-coverage). Logs only.
/// </summary>
public static class StarterChainActorAudit
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static void Report(WorldInstance world)
    {
        if (world?.Template == null || world.Template.Id != WorldManager.DefaultWorldTemplateId)
            return;

        try
        {
            foreach (var race in Enum.GetValues<Race>())
                ReportRace(world, race);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "StarterChainActors audit failed");
        }
    }

    private static void ReportRace(WorldInstance world, Race race)
    {
        if (race == Race.None || !CharacterManager.Instance.IsCreatable(race, Gender.Male))
            return;

        // characters.starting_zone_id is a zones.zone_key (nuian 179, dwarf 328, elf 129,
        // hariharan 187, ferre 184, warborn 157).
        var startZoneKey = CharacterManager.Instance.GetTemplate(race, Gender.Male).ZoneId;
        var startGroup = ZoneManager.Instance.GetZoneByKey(startZoneKey)?.GroupId ?? 0;
        if (startGroup == 0)
        {
            Logger.Debug("StarterChainActors race={0}: start zone key {1} has no zone group", race, startZoneKey);
            return;
        }

        var quests = 0;
        var acts = new List<StarterChainActorRules.ActorAct>();
        foreach (var template in QuestManager.Instance.GetTemplates())
        {
            var questGroup = ZoneManager.Instance.GetZoneById(template.ZoneId)?.GroupId ?? 0;
            if (!StarterChainActorRules.IsStarterChainQuest(template.RaceMask, template.Level, questGroup, race, startGroup))
                continue;
            quests++;
            acts.AddRange(StarterChainActorRules.ActsOf(template));
        }

        var required = StarterChainActorRules.Collect(acts);

        var missingDoodads = required.Doodads
            .Where(id => world.GetDoodadsByTemplateId(id).Count == 0)
            .ToList();

        HashSet<uint> birthPartitionTypes = null;
        if (ZoneGameDataRootResolver.TryGetRoot() != null)
        {
            var placements = ZoneSpawnerPlacementCatalog.GetAll(startZoneKey);
            if (placements.Count > 0)
                birthPartitionTypes = placements.Select(p => p.SpawnerType).ToHashSet();
        }

        var npcsWithoutSpawner = new List<uint>();
        var npcsOutsideBirthPartition = new List<uint>();
        foreach (var npcId in required.Npcs)
        {
            var spawners = NpcGameData.Instance.GetSpawnerIds(npcId);
            if (spawners == null || spawners.Count == 0)
            {
                npcsWithoutSpawner.Add(npcId);
                continue;
            }

            if (birthPartitionTypes != null && !spawners.Any(birthPartitionTypes.Contains))
                npcsOutsideBirthPartition.Add(npcId);
        }

        Logger.Info(
            "StarterChainActors race={0} startZoneKey={1} group={2} quests={3} npcs={4} doodads={5} missingDoodads={6} npcsWithoutSpawner={7} npcsOutsideBirthPartition={8}",
            race, startZoneKey, startGroup, quests, required.Npcs.Count, required.Doodads.Count,
            missingDoodads.Count, npcsWithoutSpawner.Count,
            birthPartitionTypes == null ? "n/a" : npcsOutsideBirthPartition.Count.ToString());

        foreach (var id in missingDoodads)
        {
            Logger.Warn(
                "StarterChainActors race={0}: doodad {1} has no instance in {2}; neither doodad_spawns.json nor the level pack placed it",
                race, id, world.Template.Name);
        }

        foreach (var id in npcsWithoutSpawner)
        {
            Logger.Warn(
                "StarterChainActors race={0}: npc {1} has no npc_spawner_npcs row, so no zone host can place it",
                race, id);
        }

        foreach (var id in npcsOutsideBirthPartition)
        {
            Logger.Info(
                "StarterChainActors race={0}: npc {1} is not placed in birth partition {2}; its sibling partition must be hosted",
                race, id, startZoneKey);
        }
    }
}
