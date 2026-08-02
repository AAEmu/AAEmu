using System.Numerics;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers;

public class PortalManager(ILocalizationManager localizationManager, IWorldManager worldManager, IZoneManager zoneManager, INpcManager npcManager, IObjectIdManager objectIdManager, ITaskManager taskManager) : Singleton<PortalManager>, IPortalManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<Portal>> _recalls;
    private Dictionary<uint, uint> _recallsKey;
    private Dictionary<uint, Portal> _respawns;
    private Dictionary<uint, uint> _respawnsKey;
    private Dictionary<uint, Portal> _worldGates;
    private Dictionary<uint, uint> _worldGatesKey;

    private Dictionary<uint, OpenPortalReagents> _openPortalInlandReagents;
    private Dictionary<uint, OpenPortalReagents> _openPortalOutlandReagents;
    private Dictionary<uint, DistrictReturnPoints> _districtReturnPoints;

    public List<Portal> GetRecallBySubZoneId(uint subZoneId)
    {
        return _recalls != null && _recalls.TryGetValue(subZoneId, out var recall)
            ? recall
            : null;
    }

    public Portal GetRecallById(uint returnPointId)
    {
        if (_recallsKey == null || !_recallsKey.TryGetValue(returnPointId, out var key)) { return null; }
        if (!_recalls.TryGetValue(key, out var portals)) { return null; }

        return portals.FirstOrDefault(portal => portal.Id == returnPointId);
    }

    public Portal GetRespawnBySubZoneId(uint subZoneId)
    {
        return _respawns != null && _respawns.TryGetValue(subZoneId, out var respawn)
            ? respawn
            : null;
    }

    public Portal GetRespawnById(uint id)
    {
        return _respawnsKey != null && _respawnsKey.TryGetValue(id, out var key)
            ? _respawns.GetValueOrDefault(key)
            : null;
    }

    public Portal GetWorldGatesBySubZoneId(uint subZoneId)
    {
        return _worldGates != null && _worldGates.TryGetValue(subZoneId, out var worldGate)
            ? worldGate
            : null;
    }

    public Portal GetWorldGatesById(uint id)
    {
        return _worldGatesKey != null && _worldGatesKey.TryGetValue(id, out var key)
            ? _worldGates.GetValueOrDefault(key)
            : null;
    }

    /// <summary>
    /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId
    /// </summary>
    /// <param name="districtId"></param>
    /// <returns>ReturnPointId</returns>
    public uint GetDistrictReturnPoint(uint districtId)
    {
        return (
            from point in _districtReturnPoints
            where point.Value.DistrictId == districtId
            select point.Value.ReturnPointId).FirstOrDefault();
    }

    /// <summary>
    /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId и FactionId, так как точки для фракций могут быть разные
    /// </summary>
    /// <param name="districtId"></param>
    /// <param name="factionId"></param>
    /// <returns>ReturnPointId</returns>
    public uint GetDistrictReturnPoint(uint districtId, FactionsEnum factionId)
    {
        return (
            from point in _districtReturnPoints
            where point.Value.DistrictId == districtId
            where point.Value.FactionId == factionId
            select point.Value.ReturnPointId).FirstOrDefault();
    }

    /// <summary>
    /// Inverse of <see cref="GetDistrictReturnPoint"/> — the portal-book wire <c>id</c> is the
    /// district, while <c>type</c> carries the return-point id (live SC 0x089 capture).
    /// </summary>
    public uint GetDistrictIdByReturnPoint(uint returnPointId, FactionsEnum factionId)
    {
        return (
            from point in _districtReturnPoints
            where point.Value.ReturnPointId == returnPointId
            where point.Value.FactionId == factionId
            select point.Value.DistrictId).FirstOrDefault();
    }

    public void Load()
    {
        _openPortalInlandReagents = [];
        _openPortalOutlandReagents = [];
        //_allDistrictPortals = new Dictionary<uint, Portal>();
        //_allDistrictPortalsKey = new Dictionary<uint, uint>();
        _districtReturnPoints = [];

        _recalls = [];
        _respawns = [];
        _worldGates = [];
        _recallsKey = [];
        _respawnsKey = [];
        _worldGatesKey = [];

        Logger.Info("Loading Portals ...");

        #region FileManager

        var filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "recalls.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        var contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> recalls, out _))
            foreach (var recall in recalls)
            {
                recall.Name = localizationManager.Get("return_points", "name", recall.Id, recall.Name);

                var rp = new List<Portal>();
                if (!_recalls.TryGetValue(recall.SubZoneId, out var value))
                {
                    rp.Add(recall);
                    _recalls.Add(recall.SubZoneId, rp);
                }
                else
                {
                    value.Add(recall);
                }

                if (!_recallsKey.ContainsKey(recall.Id))
                {
                    _recallsKey.Add(recall.Id, recall.SubZoneId);
                }
                else
                {
                    //
                }
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info($"Loaded {_recalls.Count} Recall Portals");

        filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "respawns.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> respawns, out _))
            foreach (var respawn in respawns)
            {
                respawn.ZoneId = worldManager.GetZoneId(worldManager.GetWorldTemplateByName("main_world"), respawn.X, respawn.Y);
                if (_respawns.ContainsKey(respawn.SubZoneId))
                {
                    //
                }
                _respawns.Add(respawn.SubZoneId, respawn);
                _respawnsKey.Add(respawn.Id, respawn.SubZoneId);
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info($"Loaded {_respawns.Count} Respawn Portals");

        filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "worldgates.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> worldGates, out _))
            foreach (var worldGate in worldGates)
            {
                _worldGates.Add(worldGate.SubZoneId, worldGate);
                _worldGatesKey.Add(worldGate.Id, worldGate.SubZoneId);
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info($"Loaded {_worldGates.Count} Worldgate Portals");

        #endregion

        #region Sqlite

        using (var connection = SQLite.CreateConnection())
        {
            // NOTE - priority -> to remove item from inventory first
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM open_portal_inland_reagents";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new OpenPortalReagents
                    {
                        Id = reader.GetUInt32("id"),
                        OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                        ItemId = reader.GetUInt32("item_id"),
                        Amount = reader.GetInt32("amount"),
                        Priority = reader.GetInt32("priority")
                    };
                    _openPortalInlandReagents.Add(template.Id, template);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM open_portal_outland_reagents";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new OpenPortalReagents
                    {
                        Id = reader.GetUInt32("id"),
                        OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                        ItemId = reader.GetUInt32("item_id"),
                        Amount = reader.GetInt32("amount"),
                        Priority = reader.GetInt32("priority")
                    };
                    _openPortalOutlandReagents.Add(template.Id, template);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM district_return_points";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new DistrictReturnPoints
                    {
                        Id = reader.GetUInt32("id"),
                        DistrictId = reader.GetUInt32("district_id"),
                        FactionId = (FactionsEnum)reader.GetUInt32("faction_id"),
                        ReturnPointId = reader.GetUInt32("return_point_id")
                    };
                    _districtReturnPoints.TryAdd(template.Id, template);
                }
            }
        }
        Logger.Info("Loaded Portal Info");
        #endregion
    }

    public static bool CheckItemAndRemove(Character owner, uint itemId, int amount)
    {
        if (!owner.Inventory.CheckItems(SlotType.Inventory, itemId, amount)) return false;
        owner.Inventory.Bag.ConsumeItem(ItemTaskType.Teleport, itemId, amount, null);
        return true;
    }

    private bool CheckCanOpenPortal(Character owner, uint targetZoneId)
    {
        var targetContinent = zoneManager.GetTargetIdByZoneId(targetZoneId);
        var ownerContinent = zoneManager.GetTargetIdByZoneId(owner.Transform.ZoneId);

        if (targetContinent == ownerContinent)
        {
            foreach (var (_, value) in _openPortalInlandReagents)
            {
                if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
            }
        }
        else
        {
            foreach (var (_, value) in _openPortalOutlandReagents)
            {
                if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
            }
        }
        return false; // Not enough items
    }

    /// <summary>open_portal_effects id 1: enter_portal_npc_id — the green portal you walk into.</summary>
    private const uint EntrancePortalNpcId = 3891;
    /// <summary>open_portal_effects id 1: exit_portal_npc_id — the yellow portal at the destination.</summary>
    private const uint ExitPortalNpcId = 6629;

    /// <summary>
    /// Create a portal Npc object and returns it
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="isExit"></param>
    /// <param name="portalInfo"></param>
    /// <param name="portalEffectObj"></param>
    /// <returns></returns>
    private Models.Game.Units.Portal MakePortal(Unit owner, bool isExit, Portal portalInfo, SkillObjectUnk1 portalEffectObj)
    {
        var portalPointDestination = new Transform(null, null, 
            portalInfo.ZoneId,
            owner.Transform.InstanceId,
            portalInfo.X, portalInfo.Y, portalInfo.Z,
            0f, 0f, portalInfo.ZRot);

        // TODO: Add support for different types of teleport books
        var templateId = isExit ? ExitPortalNpcId : EntrancePortalNpcId;
        var template = npcManager.GetTemplate(templateId);
        var portalNpc = new Models.Game.Units.Portal
        {
            ParentWorld = owner.ParentWorld,
            ObjId = objectIdManager.GetNextId(),
            OwnerId = ((Character)owner).Id,
            TemplateId = templateId,
            Template = template,
            ModelId = template.ModelId,
            Faction = owner.Faction, // INFO - FactionManager.Instance.GetFaction(template.FactionId)
            Level = template.Level,
            Name = portalInfo.Name,
            TeleportPosition = portalPointDestination,
            IsExit = isExit,
            Transform = { ZoneId = portalInfo.ZoneId }
        };

        if (isExit)
        {
            portalNpc.Transform.Local.SetPosition(portalInfo.X, portalInfo.Y, portalInfo.Z,
                0f, 0f, portalInfo.ZRot);
        }
        else
        {
            portalNpc.Transform.Local.SetPosition(
                portalEffectObj.X, portalEffectObj.Y, portalEffectObj.Z,
                owner.Transform.World.Rotation.X, owner.Transform.World.Rotation.Y, owner.Transform.World.Rotation.Z);
        }

        portalNpc.InitializeSpawnBuffs();
        portalNpc.UpdateGearBonuses(null, null);

        portalNpc.Hp = portalNpc.MaxHp;
        portalNpc.Mp = portalNpc.MaxMp;
        
        portalNpc.Spawn();

        var killTask = new KillPortalTask(portalNpc);
        taskManager.Schedule(killTask, TimeSpan.FromSeconds(30));
        return portalNpc;
    }

    public void OpenPortal(Character owner, SkillObjectUnk1 portalEffectObj)
    {
        var portalInfo = owner.Portals.GetPortalInfo((uint)portalEffectObj.Id);
        if (!CheckCanOpenPortal(owner, portalInfo.ZoneId)) return;

        var entrance = MakePortal(owner, false, portalInfo, portalEffectObj);   // Entrance (green)
        var exit = MakePortal(owner, true, portalInfo, portalEffectObj);    // Exit (yellow)
        // Linked the 2 portals
        entrance.LinkedPortal = exit;
        exit.LinkedPortal = entrance;
    }

    public static void UsePortal(Character character, uint objId)
    {
        // TODO - Cooldown between portals
        if (character.ParentWorld.GetNpc(objId) is not Models.Game.Units.Portal portal) return;

        //have Overburdened buff cannot UsePortal
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagOverburdened))
        {
            character.SendErrorMessage(ErrorMessageType.CannotUsePortalWithBackpack);
            return;
        }

        var destination = portal.TeleportPosition;
        var position = destination.World.Position;
        var yaw = destination.World.Rotation.Z.DegToRad();

        Logger.Info("UsePortal: {0} -> {1} zone {2} ({3:0.0}, {4:0.0}, {5:0.0})",
            character.Name, portal.Name, destination.ZoneId, position.X, position.Y, position.Z);

        character.SendPacket(new SCUnitPortalUsedPacket(portal.ObjId));

        if (destination.InstanceId != character.Transform.InstanceId)
        {
            // Crossing instances means a loading screen, and the client answers it with
            // CSInstanceLoaded — which is the only thing that clears DisabledSetPosition.
            character.DisabledSetPosition = true;
            character.SendPacket(
                new SCLoadInstancePacket(
                    destination.WorldId,
                    destination.ZoneId,
                    position.X,
                    position.Y,
                    position.Z,
                    destination.World.Rotation.X.DegToRad(),
                    destination.World.Rotation.Y.DegToRad(),
                    yaw
                )
            );

            character.Transform = destination.Clone(character);
        }
        else
        {
            // Same level: the client streams the new area seamlessly and never sends
            // CSInstanceLoaded, so blocking movement here would freeze the player server-side.
            // Move first — SetPosition is a no-op while DisabledSetPosition is set — so the region
            // change updates Transform.ZoneId and hands the unit over to the destination Zone.
            character.SetPosition(position.X, position.Y, position.Z, 0f, 0f, yaw);
            character.Transform.FinalizeTransform();
        }

        // TODO - ErrorMessage
        character.SendPacket(new SCTeleportUnitPacket(TeleportReason.Portal, 0,
            position.X, position.Y, position.Z, yaw));
    }

    public static void DeletePortal(Character owner, byte type, uint id)
    {
        var isPrivate = type != 1;
        var portalInfo = owner.Portals.GetPortalInfo(id);
        if (portalInfo == null) return;
        owner.Portals.RemoveFromBookPortal(portalInfo, isPrivate);
    }

    /// <summary>
    /// Gets the closest valid return portal (respawn) location for a given player
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public Portal GetClosestReturnPortal(Character character)
    {
        var currentPosition = character.Transform.World.Position;
        var distance = 999999f;
        var portal = new Portal {
            // Fail-safe coordinates
            X = currentPosition.X,
            Y = currentPosition.Y,
            Z = currentPosition.Z,
            ZoneId = character.Transform.ZoneId
        };

        foreach (var (_, value) in _respawns)
        {
            // Check against district specific faction respawns
            var districts = _districtReturnPoints.Values.Where(d => d.ReturnPointId == value.Id).ToList();
            if (districts.Count > 0)
            {
                var factions = districts.Select(d => d.FactionId).Distinct().ToList();
                if (factions.Count > 0 && !factions.Contains(character.Faction.MotherId) && !factions.Contains(character.Faction.Id))
                {
                    continue;
                }
            }

            // Check if it's a closed zone (for non-admins)
            if (character is { AccessLevel: < 100 })
            {
                var zone = zoneManager.GetZoneByKey(value.ZoneId);
                if (zone is null or { Closed: true })
                {
                    continue;
                }
            }

            // Calculate distance to player
            var portalXyz = new Vector3(value.X, value.Y, value.Z);
            var dist = MathUtil.CalculateDistance(currentPosition, portalXyz);
            if (dist >= distance)
            {
                continue;
            }
            distance = dist;
            portal = value;
        }
        return portal;
    }
}
