using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.World.Core.Relay;

/// <summary>
/// World-authors level-pack doodads from <c>cells/*/doodad.g</c> that
/// <c>doodad_spawns.json</c> never exported: quest talk/func, <c>client_doodad</c>,
/// <c>npctype://</c> scene bodies (3901 Feos 14227), and craft-order / local-development boards.
/// Coordinates come from <see cref="ZoneDoodadPlacementCatalog"/> only. Cell
/// ignore/open lists and tower DoodadAlmighty stay off the permanent plant.
/// No live NPC for those models.
/// </summary>
public static class QuestTalkDoodads
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static bool Disabled =>
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_LEVEL_PACK_DOODADS") == "1" ||
        Environment.GetEnvironmentVariable("AAEMU_DISABLE_QUEST_TALK_DOODADS") == "1";

    public static void EnsureAllWorlds()
    {
        if (Disabled)
            return;

        var worlds = WorldManager.Instance.GetWorlds();
        if (worlds == null)
            return;

        foreach (var world in worlds)
            Ensure(world);
    }

    public static void Ensure(WorldInstance world)
    {
        if (Disabled || world?.Template == null || !AppConfiguration.Instance.World.SpawnDoodads)
            return;

        var wanted = new HashSet<uint>(QuestManager.Instance.GetQuestTalkDoodadIds());
        DoodadManager.Instance.AddQuestFuncTemplateIds(wanted);
        DoodadManager.Instance.AddClientDoodadTemplateIds(wanted);
        DoodadManager.Instance.AddNpcTypeTemplateIds(wanted);
        DoodadManager.Instance.AddCraftOrderBoardTemplateIds(wanted);
        DoodadManager.Instance.AddLocalDevelopmentBoardTemplateIds(wanted);
        QuestTalkDoodadRules.ExceptTowerAlmighty(
            wanted,
            TowerDefGameData.Instance.GetDoodadAlmightyTargetIds());
        wanted.RemoveWhere(id =>
            GameScheduleManager.Instance.CheckDoodadInScheduleSpawners((int)id));
        if (wanted.Count == 0)
            return;

        var worldName = world.Template.Name;
        var catalog = ZoneDoodadPlacementCatalog.GetByTemplates(worldName, wanted);
        var places = new List<QuestTalkDoodadRules.Placement>(catalog.Count);
        foreach (var p in catalog)
        {
            if (p.IgnoredPermanent)
                continue;
            places.Add(new QuestTalkDoodadRules.Placement(p.TemplateId, p.X, p.Y, p.Z, p.YawDegrees));
        }

        var existing = ListExisting(world, wanted);
        var planned = QuestTalkDoodadRules.Plan(wanted, places, existing);
        if (planned.Count == 0)
        {
            Logger.Info(
                "LevelPackDoodads world={0} wanted={1} catalog={2} already present or no .g row",
                worldName, wanted.Count, places.Count);
            return;
        }

        var spawned = 0;
        foreach (var place in planned)
        {
            if (!DoodadManager.Instance.Exist(place.TemplateId))
            {
                Logger.Warn("LevelPackDoodads unknown doodad template {0}", place.TemplateId);
                continue;
            }

            var zoneId = WorldManager.Instance.GetZoneId(world.Template, place.X, place.Y);
            if (zoneId == 0)
            {
                Logger.Warn(
                    "LevelPackDoodads tpl={0} at {1:0.##},{2:0.##} has no zone — not spawning",
                    place.TemplateId, place.X, place.Y);
                continue;
            }

            var spawner = new DoodadSpawner
            {
                ParentWorld = world,
                Id = 0,
                UnitId = place.TemplateId,
                Position = new WorldSpawnPosition
                {
                    WorldId = world.Id,
                    X = place.X,
                    Y = place.Y,
                    Z = place.Z,
                    ZoneId = zoneId,
                    Yaw = place.YawDegrees.DegToRad()
                }
            };

            Doodad doodad;
            try
            {
                doodad = spawner.Spawn(0);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "LevelPackDoodads spawn failed tpl={0}", place.TemplateId);
                continue;
            }

            if (doodad == null || doodad.ObjId == 0)
            {
                Logger.Warn("LevelPackDoodads tpl={0} spawn returned no objId", place.TemplateId);
                continue;
            }

            spawned++;
        }

        Logger.Info(
            "LevelPackDoodads world={0} wanted={1} catalog={2} spawned={3}",
            worldName, wanted.Count, places.Count, spawned);
    }

    private static List<QuestTalkDoodadRules.Existing> ListExisting(WorldInstance world, HashSet<uint> wanted)
    {
        var list = new List<QuestTalkDoodadRules.Existing>();
        foreach (var id in wanted)
        {
            foreach (var doodad in world.GetDoodadsByTemplateId(id))
            {
                var pos = doodad.Transform?.World;
                if (pos == null)
                    continue;
                list.Add(new QuestTalkDoodadRules.Existing(id, pos.Position.X, pos.Position.Y, pos.Position.Z));
            }
        }

        return list;
    }
}
