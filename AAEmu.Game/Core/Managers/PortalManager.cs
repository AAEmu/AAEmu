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
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models;
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

public class PortalManager(ILocalizationManager localizationManager, IWorldManager worldManager, IZoneManager zoneManager, INpcManager npcManager, IObjectIdManager objectIdManager) : Singleton<PortalManager>, IPortalManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<Portal>> _recalls;
    private Dictionary<uint, uint> _recallsKey;
    private Dictionary<uint, Portal> _respawns;
    private Dictionary<uint, uint> _respawnsKey;
    private Dictionary<uint, Portal> _worldGates;
    private Dictionary<uint, uint> _worldGatesKey;
    private Dictionary<uint, Portal> _levelReturns;

    private Dictionary<uint, OpenPortalReagents> _openPortalInlandReagents;
    private Dictionary<uint, OpenPortalReagents> _openPortalOutlandReagents;
    private Dictionary<uint, DistrictReturnPoints> _districtReturnPoints;
    private readonly Dictionary<uint, HashSet<Models.Game.Units.Portal>> _ownerPortals = [];
    private readonly Lock _ownerPortalsLock = new();

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
    /// Destination for SpecialEffect Return value1 = return_points.id.
    /// Worldgate and recall JSON win; otherwise the level <c>return_point.g</c> row.
    /// </summary>
    public Portal GetReturnPoint(uint returnPointId)
    {
        var gate = GetWorldGatesById(returnPointId);
        if (gate != null)
            return gate;
        var recall = GetRecallById(returnPointId);
        if (recall != null)
            return recall;
        return _levelReturns != null && _levelReturns.TryGetValue(returnPointId, out var level)
            ? level
            : null;
    }

    /// <summary>
    /// Destinations loaded from <c>return_point.g</c> through compact <c>editor_name</c>.
    /// Memory Tome / worldgate JSON is a different id space and is not included.
    /// Empty when <c>ZoneGameDataRoot</c> is unset.
    /// </summary>
    public IReadOnlyList<Portal> GetLevelReturnPoints()
    {
        if (_levelReturns == null || _levelReturns.Count == 0)
            return [];
        return [.. _levelReturns.Values];
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
        _levelReturns = [];
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

            var editorNameToId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, editor_name FROM return_points";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var editorName = reader.GetString("editor_name", string.Empty);
                    if (string.IsNullOrWhiteSpace(editorName))
                        continue;
                    editorNameToId.TryAdd(editorName, reader.GetUInt32("id"));
                }
            }

            LoadLevelReturnPoints(editorNameToId);
        }
        Logger.Info("Loaded Portal Info");
        #endregion
    }

    private void LoadLevelReturnPoints(IReadOnlyDictionary<string, uint> editorNameToId)
    {
        var roots = EnumerateZoneGameDataRoots();
        if (roots.Count == 0)
        {
            Logger.Warn(
                "ZoneGameDataRoot is not configured — return_point.g destinations are not loaded");
            return;
        }

        var added = 0;
        foreach (var local in ReturnPointGCatalog.LoadFromRoots(roots))
        {
            if (!ReturnPointFileRules.TryGetReturnPointId(editorNameToId, local.EditorName, out var id))
                continue;
            var haveGate = _worldGatesKey.ContainsKey(id);
            var haveRecall = _recallsKey.ContainsKey(id);
            if (!ReturnPointFileRules.ShouldUseLevelFile(haveGate, haveRecall))
                continue;
            var worldTemplate = worldManager.GetWorldTemplateByZoneKey(local.ZoneKey);
            if (worldTemplate?.XmlWorldZones == null || !worldTemplate.XmlWorldZones.ContainsKey(local.ZoneKey))
            {
                Logger.Warn("return_point.g {0} zone {1} is not loaded", local.EditorName, local.ZoneKey);
                continue;
            }

            var world = zoneManager.ConvertToWorldCoordinates(
                local.ZoneKey,
                new Vector3(local.X, local.Y, local.Z));
            var portal = new Portal
            {
                Id = id,
                Type = id,
                Name = localizationManager.Get("return_points", "name", id, local.EditorName),
                ZoneId = local.ZoneKey,
                X = world.X,
                Y = world.Y,
                Z = world.Z,
                ZRot = local.ZRotRadians,
                Yaw = ReturnPointFileRules.YawDegreesFromZRot(local.ZRotRadians)
            };
            if (_levelReturns.TryAdd(id, portal))
                added++;
        }

        Logger.Info("Loaded {0} return_point.g destinations", added);
    }

    private static List<string> EnumerateZoneGameDataRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            try
            {
                var full = Path.GetFullPath(candidate.Trim());
                if (Directory.Exists(full))
                    seen.Add(full);
            }
            catch (Exception)
            {
                // bad path
            }
        }

        Offer(Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT"));
        Offer(AppConfiguration.Instance.ZoneGameDataRoot);
        return [.. seen];
    }

    public static bool CheckItemAndRemove(Character owner, uint itemId, int amount)
    {
        if (!owner.Inventory.CheckItems(SlotType.Inventory, itemId, amount)) return false;
        owner.Inventory.Bag.ConsumeItem(ItemTaskType.Teleport, itemId, amount, null);
        return true;
    }

    protected virtual bool CheckCanOpenPortal(Character owner, uint targetZoneId, uint openPortalEffectId)
    {
        var targetContinent = zoneManager.GetTargetIdByZoneId(targetZoneId);
        var ownerContinent = zoneManager.GetTargetIdByZoneId(owner.Transform.ZoneId);
        var source = targetContinent == ownerContinent ? _openPortalInlandReagents : _openPortalOutlandReagents;
        if (source == null)
        {
            Logger.Error("OpenPortal reagent tables are not loaded");
            return false;
        }

        var reagents = OpenPortalReagentRules.ForEffect(source.Values, openPortalEffectId);

        if (reagents.Count == 0)
        {
            Logger.Error("OpenPortal effect {0} has no {1} reagent rows", openPortalEffectId,
                targetContinent == ownerContinent ? "inland" : "outland");
            return false;
        }

        foreach (var reagent in reagents)
        {
            if (CheckItemAndRemove(owner, reagent.ItemId, reagent.Amount))
                return true;
        }
        return false;
    }

    protected void RegisterLivePortal(Character owner, Models.Game.Units.Portal portal)
    {
        lock (_ownerPortalsLock)
        {
            if (!_ownerPortals.TryGetValue(owner.Id, out var portals))
            {
                portals = [];
                _ownerPortals[owner.Id] = portals;
            }
            portals.Add(portal);
        }
    }

    protected virtual void DeleteLivePortal(Models.Game.Units.Portal portal) => portal.Delete();

    /// <summary>Deletes what is still live: a pair member the cascade already removed is skipped.</summary>
    private static void DeleteAlive(IEnumerable<Models.Game.Units.Portal> portals, Action<Models.Game.Units.Portal> delete)
    {
        foreach (var portal in portals)
        {
            // Portal.Delete cascades to the linked portal; without this guard the second member of the
            // pair is deleted again and is sent a second death packet.
            if (!portal.IsDeadOrDeleted)
                delete(portal);
        }
    }

    public void DeleteOwnerPortals(Character owner)
    {
        Models.Game.Units.Portal[] owned;
        lock (_ownerPortalsLock)
        {
            if (owner == null || !_ownerPortals.TryGetValue(owner.Id, out var portals))
                return;

            owned = [.. portals];
            _ownerPortals.Remove(owner.Id);
        }

        DeleteAlive(owned, DeleteLivePortal);
    }

    private void DeleteOwnerPortals(Character owner, Portal sourcePortal)
    {
        Models.Game.Units.Portal[] matched;
        lock (_ownerPortalsLock)
        {
            if (owner == null || sourcePortal == null || !_ownerPortals.TryGetValue(owner.Id, out var portals))
                return;

            matched = [.. portals.Where(portal => ReferenceEquals(portal.SourcePortal, sourcePortal))];
            foreach (var portal in matched)
                portals.Remove(portal);
            if (portals.Count == 0)
                _ownerPortals.Remove(owner.Id);
        }

        DeleteAlive(matched, DeleteLivePortal);
    }

    /// <summary>
    /// Create a portal Npc object and returns it.
    /// </summary>
    protected virtual Models.Game.Units.Portal MakePortal(Character owner, bool isExit, Portal portalInfo,
        SkillObjectUnk1 portalEffectObj, uint templateId)
    {
        var template = npcManager.GetTemplate(templateId);
        if (template == null)
        {
            Logger.Error("OpenPortal NPC template {0} is not loaded", templateId);
            return null;
        }

        var portalPointDestination = new Transform(null, null,
            portalInfo.ZoneId,
            owner.Transform.InstanceId,
            portalInfo.X, portalInfo.Y, portalInfo.Z,
            0f, 0f, portalInfo.ZRot);

        var portalNpc = new Models.Game.Units.Portal
        {
            ParentWorld = owner.ParentWorld,
            ObjId = objectIdManager.GetNextId(),
            OwnerId = owner.Id,
            TemplateId = templateId,
            Template = template,
            ModelId = template.ModelId,
            Faction = owner.Faction,
            Level = template.Level,
            Name = portalInfo.Name,
            TeleportPosition = portalPointDestination,
            IsExit = isExit,
            SourcePortal = portalInfo,
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
        return portalNpc;
    }

    public void OpenPortal(Character owner, SkillObjectUnk1 portalEffectObj, OpenPortalEffect effect)
    {
        if (owner == null || portalEffectObj == null || effect == null)
            return;

        if (!OpenPortalNpcRules.TryResolve(effect, out var enterNpcId, out var exitNpcId))
        {
            Logger.Error("OpenPortal effect {0} is missing an enter/exit NPC template", effect.Id);
            return;
        }

        if (npcManager.GetTemplate(enterNpcId) == null || npcManager.GetTemplate(exitNpcId) == null)
        {
            Logger.Error("OpenPortal effect {0} references an NPC template that is not loaded", effect.Id);
            return;
        }

        if (portalEffectObj.Id < 0)
        {
            Logger.Warn("OpenPortal effect {0} received a negative portal id", effect.Id);
            return;
        }

        // The cast names the book in portalEffectObj.Type, so resolve through the typed lookup:
        // a private entry that collides with a visited district id must not open the district one.
        var portalInfo = owner.Portals.GetPortalInfoByType((uint)portalEffectObj.Id, portalEffectObj.Type);
        if (portalInfo == null)
        {
            Logger.Warn("OpenPortal effect {0} cannot resolve portal id {1}", effect.Id, portalEffectObj.Id);
            return;
        }

        if (!CheckCanOpenPortal(owner, portalInfo.ZoneId, effect.Id))
            return;

        var entrance = MakePortal(owner, false, portalInfo, portalEffectObj, enterNpcId);
        if (entrance == null)
            return;

        var exit = MakePortal(owner, true, portalInfo, portalEffectObj, exitNpcId);
        if (exit == null)
        {
            entrance.Delete();
            return;
        }

        entrance.LinkedPortal = exit;
        exit.LinkedPortal = entrance;

        // A previous pair for the same book entry is now redundant: the new one serves the same
        // destination. Removing it bounds a live portal set to one pair per book entry, which is
        // what a client-side expiry would have achieved, without inventing a duration that no
        // shipped content or protocol value provides.
        DeleteOwnerPortals(owner, portalInfo);

        RegisterLivePortal(owner, entrance);
        RegisterLivePortal(owner, exit);
    }

    public static void UsePortal(Character character, uint objId, bool onlyMyPortal = false)
    {
        // No cooldown is applied here: no authoritative content/protocol value has been identified.
        if (character.ParentWorld.GetNpc(objId) is not Models.Game.Units.Portal portal) return;

        // The client's onlyMyPortal flag asks the server to restrict the use to the owner's own
        // portal. Portals now outlive a single cast, so without this a stale pair stays usable by
        // anyone who walks into it. A portal with no book entry (a GM-created one) has no owner
        // to compare against, so the flag is not enforceable for it.
        var portalOwnerId = portal.SourcePortal?.Owner;
        if (onlyMyPortal && portalOwnerId != character.Id)
        {
            Logger.Warn("UsePortal: character {0} refused portal {1} owned by {2} because onlyMyPortal was set",
                character.Id, portal.ObjId, portalOwnerId ?? 0);
            return;
        }

        //have Overburdened buff cannot UsePortal
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagOverburdened))
        {
            character.SendErrorMessage(ErrorMessageType.CannotUsePortalWithBackpack);
            return;
        }

        var destination = portal.TeleportPosition;
        var position = destination.World.Position;

        Logger.Info("UsePortal: {0} -> {1} zone {2} ({3:0.0}, {4:0.0}, {5:0.0})",
            character.Name, portal.Name, destination.ZoneId, position.X, position.Y, position.Z);

        character.SendPacket(new SCUnitPortalUsedPacket(portal.ObjId));

        var destWorld = portal.LinkedPortal?.ParentWorld ?? portal.ParentWorld ?? character.ParentWorld;
        if (!SkillTeleportLanding.TryApplyToWorld(
                character,
                destWorld,
                destination.ZoneId,
                position.X,
                position.Y,
                position.Z,
                destination.World.Rotation.Z,
                TeleportReason.Portal))
        {
            Logger.Warn("UsePortal: {0} could not land at portal {1} zone {2}",
                character.Name, portal.Name, destination.ZoneId);
        }
    }

    public void DeletePortal(Character owner, byte type, uint id)
    {
        var isPrivate = type != 1;
        // The wire type says which book the client named. A type-agnostic lookup answers from the
        // district book first, so deleting a private entry would tear down the district entry's live
        // pair and leave the private one standing.
        var portalInfo = isPrivate
            ? owner.Portals.GetPrivatePortalInfo(id)
            : owner.Portals.GetDistrictPortalInfo(id);
        if (portalInfo == null)
            return;

        owner.Portals.RemoveFromBookPortal(portalInfo, isPrivate);
        // Match the book entry, not its id: the two id spaces overlap.
        DeleteOwnerPortals(owner, portalInfo);
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
