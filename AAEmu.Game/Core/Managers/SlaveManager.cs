using System.Numerics;

using AAEmu.Commons.Utils.DB;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Slave;
using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SlaveManager(WorldInstance parentWorldInstance)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private WorldInstance World { get; init; } = parentWorldInstance;

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _slaveListLock = new();

    /// <summary>Headings tried when looking for water to summon a boat onto, centred on the caster's own.</summary>
    private const int HeadingSweepSteps = 25;

    private const float HeadingSweepStepRadians = MathF.PI / 12f; // 15°

    /// <summary>
    /// Water surface at a position, resolved from the ingested bodies themselves.
    /// </summary>
    /// <remarks>
    /// <see cref="WaterBodies.GetWaterSurface"/> answers with the ocean plane at Gweonid even though the
    /// lake polygon there contains the point and reports a surface of 202.8, which drops a summoned hull
    /// ~100m to sea level. Querying the areas directly gives the surface the polygons actually describe.
    /// </remarks>
    private static float GetWaterSurfaceFromAreas(WorldInstance world, Vector3 position)
        => GetWaterSurfaceFromAreas(world, world.Water.GetAreasSnapshot(), position);

    /// <summary>
    /// As above, over a snapshot taken once by the caller. The summon sweep probes over a thousand
    /// points, and re-copying the area list for each one dominated the cost of summoning a boat.
    /// </summary>
    private static float GetWaterSurfaceFromAreas(
        WorldInstance world, List<WaterBodyArea> areas, Vector3 position)
    {
        var best = float.NaN;
        foreach (var area in areas)
        {
            if (!area.GetSurface(position, out var surfacePoint, out _))
                continue;
            if (position.Z < surfacePoint.Z - area.Depth)
                continue;
            if (float.IsNaN(best) || MathF.Abs(surfacePoint.Z - position.Z) < MathF.Abs(best - position.Z))
                best = surfacePoint.Z;
        }

        return float.IsNaN(best) ? world.Water.OceanLevel : best;
    }

    public Slave GetActiveSlaveByOwnerObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            return slaves.FirstOrDefault(slave => slave.Summoner?.ObjId == objId && !slave.IsDead);
        }
    }

    /// <summary>
    /// Returns a list of all Slaves of specific SlaveKind
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="worldId">When set, only return from specific world</param>
    /// <returns></returns>
    public IEnumerable<Slave> GetActiveSlavesByKind(SlaveKind kind, uint worldId = uint.MaxValue)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            if (worldId >= uint.MaxValue)
            {
                return slaves.Where(s => s.Template.SlaveKind == kind);
            }

            return slaves.Where(s => s.Template.SlaveKind == kind && s.Transform.WorldId == worldId);
        }
    }

    /// <summary>
    /// Returns a list of all Slaves of specific SlaveKind
    /// </summary>
    /// <param name="kinds"></param>
    /// <param name="worldId">When set, only return from specific world</param>
    /// <returns></returns>
    public IEnumerable<Slave> GetActiveSlavesByKinds(SlaveKind[] kinds, uint worldId = uint.MaxValue)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            if (worldId >= uint.MaxValue)
                return slaves.Where(s => kinds.Contains(s.Template.SlaveKind))
                    .Select(s => s);

            return slaves.Where(s => kinds.Contains(s.Template.SlaveKind))
                .Where(s => s.Transform.WorldId == worldId)
                .Select(s => s);
        }
    }

    private Slave GetSlaveByTlId(uint tlId)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            foreach (var slave in slaves.Where(slave => slave.TlId == tlId))
            {
                return slave;
            }
            return null;
        }
    }

    /// <summary>Public lookup used by CSChangeSlaveEquipment.</summary>
    public Slave FindSlaveByTlId(ushort tlId) => GetSlaveByTlId(tlId);

    /// <summary>Public lookup used by CSChangeSlaveEquipment when client Tl is missing.</summary>
    public Slave FindSlaveByDbId(uint dbId) => GetSlaveByDbId(dbId);

    public Slave GetSlaveByObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            foreach (var slave in slaves.Where(slave => slave.ObjId == objId))
            {
                return slave;
            }
        }
        return null;
    }

    private Slave GetSlaveByDbId(uint dbId)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            foreach (var slave in slaves.Where(slave => slave.Id == dbId))
            {
                return slave;
            }
        }
        return null;
    }

    /// <summary>
    /// Unmounts a player from a vehicle
    /// </summary>
    /// <param name="character"></param>
    /// <param name="tlId"></param>
    /// <param name="reason"></param>
    public void UnbindSlave(Character character, uint tlId, AttachUnitReason reason)
    {
        var slave = GetSlaveByTlId(tlId);
        if (slave == null)
        {
            character.Transform.Parent = null;
            character.Transform.StickyParent = null;
            character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
            character.AttachedPoint = AttachPointKind.None;
            character.BroadcastPacket(new SCUnitDetachedPacket(character.ObjId, reason), true);
            WorldIntegration.RelayUnitAttachToZone?.Invoke(character.ObjId, 0, 0, false);
            return;
        }

        var attachPoint = slave.AttachedCharacters.FirstOrDefault(x => x.Value == character).Key;
        if (attachPoint != default)
        {
            slave.AttachedCharacters.Remove(attachPoint);
            character.Transform.Parent = null;
            character.Transform.StickyParent = null;
            ShipHarpoonRopeController.OnOperatorLeftSlave(slave, character);
        }

        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
        character.AttachedPoint = AttachPointKind.None;

        character.BroadcastPacket(new SCUnitDetachedPacket(character.ObjId, reason), true);
        WorldIntegration.RelayUnitAttachToZone?.Invoke(character.ObjId, slave.ObjId, (byte)attachPoint, false);
        if (WorldIntegration.ZoneAuthority && attachPoint == AttachPointKind.Driver && slave.Template.IsABoat())
            WorldIntegration.RelayShipControlChangeToZone?.Invoke(slave.ObjId, false);
    }

    /// <summary>
    /// Mounts a player on a vehicle
    /// </summary>
    /// <param name="character"></param>
    /// <param name="objId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="bondKind"></param>
    public void BindSlave(Character character, uint objId, AttachPointKind attachPoint, AttachUnitReason bondKind)
    {
        // Check if the target spot is already taken
        var slave = GetSlaveByObjId(objId);

        if (slave == null || slave.IsDead || slave.AttachedCharacters.ContainsKey(attachPoint))
            return;

        // Check if the vehicle has the MasterOwnership buff and if the character is not the owner, block the attachment.
        if (attachPoint == AttachPointKind.Driver && slave.Buffs.CheckBuff((uint)BuffConstants.OwnersMark) && slave.Summoner?.ObjId != character.ObjId)
        {
            character.SendErrorMessage(ErrorMessageType.SlaveAlreadyHasMaster); // 仅阻止驾驶座附加
            return;
        }

        character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, bondKind, objId), true);
        character.AttachedPoint = attachPoint;

        // for every SCSlaveBound it receives; the "bound recieved, but not requested" line it logs when
        // no CSBindSlave is pending is only a warning and does not stop it. Withholding the packet
        // therefore leaves the client with no bound slave at all, so it is always sent for the driver.
        // Ship control is hull-only — Driver on SlaveEquipment (sails/cannons) must not steer Zone.
        if (attachPoint == AttachPointKind.Driver)
        {
            character.BroadcastPacket(
                new SCSlaveBoundPacket(character.Id, slave.MasterWorldId, objId), true);
            if (WorldIntegration.ZoneAuthority && slave.Template.IsABoat())
                WorldIntegration.RelayShipControlChangeToZone?.Invoke(objId, true);
        }

        slave.AttachedCharacters.Add(attachPoint, character);
        character.Transform.Parent = slave.Transform;
        if (!ApplyAttachPointLocation(slave, character, attachPoint))
            character.Transform.Local.SetPosition(0, 0, 0, 0, 0, 0);
        WorldIntegration.RelayUnitAttachToZone?.Invoke(character.ObjId, objId, (byte)attachPoint, true);
    }

    /// <summary>
    /// Mounts a player on a vehicle
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId">Slave TlId</param>
    public void BindSlave(GameConnection connection, uint tlId)
    {
        var unit = connection.ActiveChar;
        if (unit == null)
            return;

        var slave = GetSlaveByTlId(tlId);
        if (slave == null || slave.IsDead)
            return;

        BindSlave(unit, slave.ObjId, AttachPointKind.Driver, AttachUnitReason.NewMaster);
    }

    /// <summary>
    /// Removes a slave from the world after validating the requesting owner.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="objId"></param>
    /// <param name="ignoreAttachedItemWarning">If true will not fail if there are attached items</param>
    public void Delete(Character owner, uint objId, bool ignoreAttachedItemWarning)
    {
        var slaveInfo = GetSlaveByObjId(objId);
        if (slaveInfo == null)
            return;

        if (owner != null &&
            slaveInfo.Summoner?.ObjId != owner.ObjId &&
            slaveInfo.OwnerObjId != owner.ObjId)
        {
            Logger.Warn(
                "Rejected slave despawn obj={0} summoner={1} from {2} ({3})",
                objId, slaveInfo.Summoner?.ObjId ?? slaveInfo.OwnerObjId, owner.Name, owner.ObjId);
            return;
        }

        slaveInfo.Save();
        // Remove passengers
        foreach (var character in slaveInfo.AttachedCharacters.Values.ToList())
            UnbindSlave(character, slaveInfo.TlId, AttachUnitReason.SlaveBinding);

        // Block despawn only when a doodad is holding a real item instance (trade pack / backpack).
        // ItemTemplateId alone is not enough — persistent / visual doodads can carry a template id
        // without a pack, and refusing despawn here left the hull in the world while Create() still
        // spawned a second ship on top of it.
        if (!ignoreAttachedItemWarning)
        {
            foreach (var doodad in slaveInfo.AttachedDoodads)
            {
                if (doodad.ItemId != 0)
                {
                    owner?.SendErrorMessage(ErrorMessageType.SlaveEquipmentLoadedItem);
                    return;
                }
            }
        }

        var despawnDelayedTime = DateTime.UtcNow.AddSeconds(slaveInfo.Template.PortalTime - 0.5f);

        slaveInfo.Transform.DetachAll();

        foreach (var doodad in slaveInfo.AttachedDoodads)
        {
            // Note, we un-check the persistent flag here, or else the doodad will delete itself from DB as well
            // This is not desired for player owned slaves
            if (owner != null)
                doodad.IsPersistent = false;
            doodad.Despawn = despawnDelayedTime;
            World.SpawnManager.AddDespawn(doodad);
            // doodad.Delete();
        }

        foreach (var attachedSlave in slaveInfo.AttachedSlaves)
        {
            lock (_slaveListLock)
                World.RemoveObject(attachedSlave);
            attachedSlave.Despawn = despawnDelayedTime;
            World.SpawnManager.AddDespawn(attachedSlave);
            //attachedSlave.Delete();
        }

        owner?.BroadcastPacket(new SCSlaveDespawnPacket(objId), true);
        owner?.BroadcastPacket(new SCSlaveRemovedPacket(owner.ObjId, slaveInfo.TlId), true);

        // Otherwise the dedicate keeps simulating (and streaming) a hull World no longer has, and the
        // next summon of the same objId ends up with two owners.
        WithdrawBoatFromZone(slaveInfo);

        lock (_slaveListLock)
        {
            World.RemoveObject(slaveInfo);
        }

        slaveInfo.Despawn = DateTime.UtcNow.AddSeconds(slaveInfo.Template.PortalTime + 0.5f);
        World.SpawnManager.AddDespawn(slaveInfo);
    }

    /// <summary>
    /// Slave created from spawn effect (e.g. test vehicle from Mirage)
    /// </summary>
    /// <param name="subType">TemplateId</param>
    /// <param name="hideSpawnEffect">Suppresses the client's portal fx (UnitState flags bit 11).</param>
    /// <param name="positionOverride"></param>
    public Slave Create(uint subType, bool hideSpawnEffect = false, Transform positionOverride = null)
    {
        var slave = Create(null, null, subType, null, hideSpawnEffect, positionOverride);

        return slave;
    }

    /// <summary>
    /// Slave created from spawn effect
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="skillData"></param>
    /// <param name="hideSpawnEffect">Suppresses the client's portal fx (UnitState flags bit 11).</param>
    /// <param name="positionOverride"></param>
    public void Create(Character owner, SkillItem skillData, bool hideSpawnEffect = false, Transform positionOverride = null)
    {
        if (owner == null || skillData == null)
            return;

        if (skillData.ItemId == 0 || skillData.ItemTemplateId == 0)
            return;

        if (skillData.SkillSourceItem?.Template is not SummonSlaveTemplate itemTemplate)
            return;

        // Active-slave replace + land-in-water checks live in the main Create path.
        Create(owner, null, itemTemplate.SlaveId, skillData.SkillSourceItem, hideSpawnEffect, positionOverride);
    }

    // added "/slave spawn <templateId>" to be called from the script command
    /// <summary>
    /// Slave created by player or spawn effect, use either useSpawner or templateId
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="useSpawner"></param>
    /// <param name="templateId"></param>
    /// <param name="item"></param>
    /// <param name="hideSpawnEffect">Suppresses the client's portal fx (UnitState flags bit 11).</param>
    /// <param name="positionOverride"></param>
    /// <returns>Newly created Slave</returns>
    public Slave Create(Character owner, SlaveSpawner useSpawner, uint templateId, Item item = null, bool hideSpawnEffect = false, Transform positionOverride = null)
    {
        var slaveTemplate = SlaveGameData.Instance.GetSlaveTemplate(useSpawner?.UnitId ?? templateId);
        if (slaveTemplate == null) return null;

        // Refuse land vehicles while the caster is in water — before touching any active hull.
        if (owner != null && item != null && !slaveTemplate.IsABoat())
        {
            var checkPos = positionOverride?.World.Position ?? owner.Transform.World.Position;
            if (World.IsWater(checkPos))
            {
                Logger.Warn(
                    "SlaveSpawn land template={0} refused: caster in water at ({1:0.0},{2:0.0},{3:0.0})",
                    slaveTemplate.Id, checkPos.X, checkPos.Y, checkPos.Z);
                owner.SendErrorMessage(ErrorMessageType.SlaveSpawnErrorInvalidArea);
                return null;
            }
        }

        // Player item summons: one active hull only. Replace first; abort if despawn refuses.
        if (owner != null && item != null)
        {
            var existing = GetActiveSlaveByOwnerObjId(owner.ObjId);
            if (existing != null)
            {
                existing.Save();
                var existingObjId = existing.ObjId;
                Delete(owner, existingObjId, false);
                if (GetSlaveByObjId(existingObjId) != null)
                {
                    Logger.Warn(
                        "Create refused: active slave obj={0} still present for {1}",
                        existingObjId, owner.Name);
                    return null;
                }
            }
        }

        var tlId = (ushort)TlIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();

        using var spawnPos = positionOverride ?? new Transform(null);
        spawnPos.InstanceId = World.Id;
        var spawnOffsetPos = new Vector3();

        var dbId = 0u;
        var slaveName = string.Empty;
        var slaveHp = 1;
        var slaveMp = 1;
        var isLoadedPlayerSlave = false;
        // Item detail SlaveDbId is the durable link after the first successful summon. Prefer it over
        // slaves.item_id — a mismatched/zero item_id (GM re-grant, REPLACE quirks) used to mint a new
        // CharacterId, seed the starter pack, and orphan the customized EquipmentSlave container.
        var boundSlaveDbId = item is SummonSlave summonLink ? summonLink.SlaveDbId : 0u;

        // Check if there's already a slave attached to the summon item (if any)
        #region load_saved_slave
        if (owner?.Id > 0 && (boundSlaveDbId > 0 || item?.Id > 0))
        {
            using var connection = MySQL.CreateConnection();

            // 1) Prefer SlaveDbId from item details (survives item_id drift).
            if (boundSlaveDbId > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT * FROM slaves WHERE (owner_type = 0) AND (summoner = @playerId) AND (id = @slaveId) LIMIT 1";
                command.Parameters.AddWithValue("@playerId", owner.Id);
                command.Parameters.AddWithValue("@slaveId", boundSlaveDbId);
                command.Prepare();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    dbId = reader.GetUInt32("id");
                    slaveName = reader.GetString("name");
                    slaveHp = reader.GetInt32("hp");
                    slaveMp = reader.GetInt32("mp");
                    isLoadedPlayerSlave = true;
                }
            }

            // 2) Fall back to slaves.item_id (legacy scrolls / empty detail before first summon).
            if (!isLoadedPlayerSlave && item?.Id > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT * FROM slaves WHERE (owner_type = 0) AND (owner_id = @playerId) AND (summoner = @playerId) AND (item_id = @itemId) LIMIT 1";
                command.Parameters.AddWithValue("@playerId", owner.Id);
                command.Parameters.AddWithValue("@itemId", item.Id);
                command.Prepare();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    dbId = reader.GetUInt32("id");
                    slaveName = reader.GetString("name");
                    slaveHp = reader.GetInt32("hp");
                    slaveMp = reader.GetInt32("mp");
                    isLoadedPlayerSlave = true;
                    if (boundSlaveDbId > 0 && boundSlaveDbId != dbId)
                    {
                        Logger.Warn(
                            "SlaveSpawn: item {0} SlaveDbId={1} missed row; recovered via item_id → slave {2}",
                            item.Id, boundSlaveDbId, dbId);
                    }
                }
            }

            // 3) Detail points at an id with no row (gear container may still use that mate_id).
            if (!isLoadedPlayerSlave && boundSlaveDbId > 0)
            {
                dbId = boundSlaveDbId;
                Logger.Warn(
                    "SlaveSpawn: item {0} SlaveDbId={1} has no slaves row for {2}; reusing id (no starter seed)",
                    item?.Id ?? 0, boundSlaveDbId, owner.Name);
            }
        }
        #endregion

        // Put it at the correct location
        if (spawnPos.Local.IsOrigin())
        {
            if (owner == null && useSpawner == null)
            {
                Logger.Warn($"Tried creating a slave without a defined position, either use a Owner, Spawner or PositionOverride");
                return null;
            }

            if (useSpawner != null)
            {
                spawnPos.ApplyWorldSpawnPosition(useSpawner.Position, WorldManager.DefaultInstanceId);
            }
            else
            {
                spawnPos.ApplyWorldTransformToLocalPosition(owner.Transform, owner.Transform.InstanceId);
            }

            // If no spawn position override has been provided, then handle normal spawning algorithm

            // owner.SendMessage("SlaveSpawnOffset: x:{0} y:{1}", slaveTemplate.SpawnXOffset, slaveTemplate.SpawnYOffset);
            if (owner != null)
            {
                spawnPos.Local.AddDistanceToFront(Math.Clamp(slaveTemplate.SpawnYOffset, 5f, 50f));
            }
            // INFO: Seems like X offset is defined as the size of the vehicle summoned, but visually it's nicer if we just ignore this 
            // spawnPos.Local.AddDistanceToRight(slaveTemplate.SpawnXOffset);
            if (slaveTemplate.IsABoat())
            {
                // If we're spawning a boat, put it on the nearest ingested water surface (ocean, river, or lake).
                var world = WorldManager.Instance.GetWorld(spawnPos.InstanceId);
                if (world == null)
                {
                    Logger.Fatal($"Unable to find world to spawn in {spawnPos.WorldId}");
                    return null;
                }

                // Probe from where the caster stands. Water bodies are only reported to callers at or
                // above their own floor, so sampling after the hull has been dropped to sea level makes
                // every lake and river sit unreachably far above the query and read as open ocean.
                var casterLevelPos = spawnPos.World.Position;
                var worldWaterLevel = GetWaterSurfaceFromAreas(world, casterLevelPos);
                spawnPos.Local.SetHeight(worldWaterLevel);


                // temporary grab ship information so that we can use it to find a suitable spot in front to summon it
                var tempShipModel = ModelManager.Instance.GetShipModel(slaveTemplate.ModelId);
                var minDepth = 5f;
                if (tempShipModel != null)
                    minDepth = tempShipModel.MassBoxSizeZ - tempShipModel.MassCenterZ + 1f;

                // Standalone Game physics used MassCenter/Keel to pre-settle the hull; under ZoneAuthority
                // the dedicate owns boat buoyancy and that offset sinks the SC spawn ~2m (yawl MassCenterZ=-4).
                // Place on the water surface; Zone corrects trim when boarded.
                if (tempShipModel != null && !WorldIntegration.ZoneAuthority)
                {
                    spawnOffsetPos.Z += (tempShipModel.MassCenterZ < 0f ? tempShipModel.MassCenterZ / 2f : 0f) -
                                        tempShipModel.KeelHeight;
                }

                // Sweep outwards from the caster rather than only along their facing: a player summoning
                // from a bank is rarely aimed squarely at open water, and a forward-only probe reports
                // "no water" for a lake that is a few metres to the side.
                var searchRange = 50f + (tempShipModel?.MassBoxSizeX ?? 10f);
                var sweepOrigin = (Position: casterLevelPos, Rotation: spawnPos.World.Rotation);
                var waterAreas = world.Water.GetAreasSnapshot();
                var foundNavigableWater = false;
                for (var distance = 5f; distance <= searchRange && !foundNavigableWater; distance += 1f)
                {
                    for (var step = 0; step < HeadingSweepSteps; step++)
                    {
                        // 0, +15, -15, +30, -30 ... so the caster's own heading still wins ties.
                        var yawOffset = (step + 1) / 2 * HeadingSweepStepRadians * (step % 2 == 0 ? 1f : -1f);
                        var yaw = sweepOrigin.Rotation.Z + yawOffset;

                        // Offset built here rather than through a cloned Transform: the clone's world
                        // position does not pick up a local translation, so every probe re-sampled the
                        // origin and the scan could only ever report whatever was under the caster.
                        var probePos = new Vector3(
                            sweepOrigin.Position.X - distance * MathF.Sin(yaw),
                            sweepOrigin.Position.Y + distance * MathF.Cos(yaw),
                            sweepOrigin.Position.Z);

                        var floorHeight = World.Template.GeoData.GetHeight(probePos);
                        if (floorHeight <= 0f)
                            continue;

                        var surfaceHeight = GetWaterSurfaceFromAreas(world, waterAreas, probePos);
                        if (surfaceHeight - floorHeight <= minDepth)
                            continue;

                        spawnPos.Local.SetPosition(probePos.X, probePos.Y, surfaceHeight);
                        foundNavigableWater = true;
                        break;
                    }
                }

                if (!foundNavigableWater)
                {

                    // GetWaterSurface reports the ocean plane for any coordinate, including dry land far
                    // above it, so without this the hull is placed at sea level directly beneath a player
                    // standing inland - buried in the terrain and invisible, with no error to explain it.
                    Logger.Warn(
                        "SlaveSpawn boat template={0} refused: no water at least {1:0.0} deep within {2:0.0}m of ({3:0.0},{4:0.0}); ground {5:0.0}, surface {6:0.0}",
                        slaveTemplate.Id, minDepth, searchRange,
                        spawnPos.World.Position.X, spawnPos.World.Position.Y,
                        World.Template.GeoData.GetHeight(spawnPos.World.Position), worldWaterLevel);
                    owner?.SendErrorMessage(ErrorMessageType.SlaveSpawnErrorInvalidArea);
                    return null;
                }

                spawnPos.Local.Position += spawnOffsetPos;

            }
            else
            {
                // Land vehicles cannot be summoned while the caster is in water (boats use the
                // branch above). Without this check a car could replace a stuck ship mid-ocean.
                var checkPos = owner?.Transform.World.Position ?? spawnPos.World.Position;
                if (World.IsWater(checkPos))
                {
                    Logger.Warn(
                        "SlaveSpawn land template={0} refused: caster in water at ({1:0.0},{2:0.0},{3:0.0})",
                        slaveTemplate.Id, checkPos.X, checkPos.Y, checkPos.Z);
                    owner?.SendErrorMessage(ErrorMessageType.SlaveSpawnErrorInvalidArea);
                    return null;
                }

                // Land vehicle: prefer the summoner's floor. HeightMapsEnable is often false under
                // ZoneAuthority, and GeoData/.bai nearest-node samples can sit ~1–2 m above the
                // dirt the player is standing on — that is exactly the "hovering car" look.
                var ownerZ = owner?.Transform.World.Position.Z ?? spawnPos.World.Position.Z;
                var h = World.Template.GeoData.GetHeight(spawnPos.World.Position);
                if (h > 0f && MathF.Abs(h - ownerZ) <= 2f)
                    spawnPos.Local.SetHeight(h);
                else
                    spawnPos.Local.SetHeight(ownerZ);
            }

            // Always spawn horizontal(level) and 90° CCW of the player
            spawnPos.Local.SetRotation(0f, 0f, owner?.Transform.World.Rotation.Z + MathF.PI / 2 ?? useSpawner.Position.Yaw);
        }

        // Get new Id to save if it has a player as owner (never mint one when SlaveDbId already binds the scroll).
        if (owner?.Id > 0 && dbId <= 0)
            dbId = CharacterIdManager.Instance.GetNextId(); // CharacterIdManager uses both character and slave IDs to populate

        // Update the summoning item
        if (item is SummonSlave slaveSummonItem)
        {
            slaveSummonItem.SlaveType = 0x02;
            slaveSummonItem.SlaveDbId = dbId;
            if (slaveSummonItem.IsDestroyed > 0 || slaveSummonItem.RepairStartTime > DateTime.MinValue)
            {
                var secondsLeft = (slaveSummonItem.RepairStartTime.AddMinutes(10) - DateTime.UtcNow).TotalSeconds;
                if (secondsLeft > 0.0)
                {
                    // Slave was destroyed and is on cooldown
                    owner?.SendErrorMessage(ErrorMessageType.SlaveSpawnErrorNeedRepairTime, (uint)Math.Round(secondsLeft));
                    return null;
                }
            }
            slaveSummonItem.SummonLocation = spawnPos.World.Position;
            slaveSummonItem.RepairStartTime = DateTime.MinValue; // reset timer here
            slaveSummonItem.IsDirty = true;
            owner?.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.UpdateSummonSlaveItem, new ItemUpdate(item), []));
        }

        // Create the Slave (packet)
        #region spawn_base_slave
        owner?.BroadcastPacket(new SCSlaveCreatedPacket(owner.ObjId, tlId, objId, 0, owner.Name), true);
        var summonedSlave = new Slave
        {
            ParentWorld = World,
            TlId = tlId,
            ObjId = objId,
            TemplateId = slaveTemplate.Id,
            Name = string.IsNullOrWhiteSpace(slaveName) ? slaveTemplate.Name : slaveName,
            Level = (byte)slaveTemplate.Level,
            ModelId = slaveTemplate.ModelId,
            Template = slaveTemplate,
            Hp = slaveHp,
            Mp = slaveMp,
            Faction = owner?.Faction ?? FactionManager.Instance.GetFaction(slaveTemplate.FactionId),
            Id = dbId,
            Summoner = owner,
            SummoningItem = item,
            SpawnTime = DateTime.UtcNow,
            Spawner = useSpawner,
            OwnerType = owner != null ? BaseUnitType.Character : BaseUnitType.Invalid,
            OwnerId = owner?.Id ?? 0,
            OwnerObjId = owner?.ObjId ?? 0,
        };

        ApplySlaveBonuses(summonedSlave);

        // If it was loaded from DB, restore previous its HP/MP
        if (!isLoadedPlayerSlave)
        {
            summonedSlave.Hp = summonedSlave.MaxHp;
            summonedSlave.Mp = summonedSlave.MaxMp;
        }

        // Customized ship gear has to survive a despawn, so bind the container to the slave's DB id the
        // same way mates do — item_containers.mate_id doubles as the slave id, the container type keeps
        // the two apart. Without this the parts live in a throwaway container, get written back with
        // container_id 0 and are orphaned on the next restart.
        var isNewEquipment = true;
        if (owner?.Id > 0 && summonedSlave.Id > 0)
        {
            var existingEquipment = ItemManager.Instance.FindItemContainerFor(
                owner.Id, SlotType.EquipmentSlave, summonedSlave.Id);
            // Starter pack only for a never-bound hull. A saved SlaveDbId, an existing container, or
            // any already-equipped parts means this is not a first-time ship — re-seeding would replace
            // customized gear with defaults (the "first summon after restart looks stock" bug).
            isNewEquipment = existingEquipment == null
                             && !isLoadedPlayerSlave
                             && boundSlaveDbId == 0;

            summonedSlave.Equipment = ItemManager.Instance.GetItemContainerForCharacter(
                owner.Id, SlotType.EquipmentSlave, summonedSlave, summonedSlave.Id);
            summonedSlave.Equipment.ContainerSize = 32; // slave_equip_slots goes up to 31

            if (!isNewEquipment && existingEquipment != null)
            {
                Logger.Info(
                    "SlaveSpawn: reused EquipmentSlave container={0} items={1} slaveDb={2} item={3}",
                    existingEquipment.ContainerId, existingEquipment.Items.Count, summonedSlave.Id, item?.Id ?? 0);
            }
            else if (isNewEquipment)
            {
                Logger.Info(
                    "SlaveSpawn: seeding starter pack for new slaveDb={0} item={1} tpl={2}",
                    summonedSlave.Id, item?.Id ?? 0, summonedSlave.TemplateId);
            }
        }

        // Seed default equipment into Slave.Equipment (drives Customize UI via SCUnitState). World meshes
        // for those items spawn later via SpawnEquipmentVisualsFromInventory. Only ever seeded once per
        // ship: re-seeding a saved ship would hand out a free copy of every part it was stripped of.
        var itemPack = SlaveGameData.Instance.GetSlaveInitialItemPack(summonedSlave.Template.SlaveInitialItemPackId);
        if (itemPack != null && isNewEquipment)
        {
            foreach (var initialItem in itemPack)
            {
                // Real item ids required — Id=0 items cannot be found again to unequip or swap.
                var newItem = ItemManager.Instance.Create(initialItem.itemId, 1, 0, true);
                summonedSlave.Equipment.AddOrMoveExistingItem(ItemTaskType.Invalid, newItem, initialItem.equipSlotId);
            }
        }

        // BindOnEquip for EquipmentSlave used to be a no-op, so starter parts and previously-equipped
        // BoE gear stayed unbound (client re-prompted every equip). Sweep the hull after seed/load.
        summonedSlave.Equipment?.ApplyBindRules(ItemTaskType.UpdateSummonSlaveItem);

        // Camp HP/MP values as needed 
        summonedSlave.Hp = Math.Min(summonedSlave.Hp, summonedSlave.MaxHp);
        summonedSlave.Mp = Math.Min(summonedSlave.Mp, summonedSlave.MaxMp);

        // Reset HP on "dead" vehicles (can't summon with 0 HP)
        if (summonedSlave.Hp <= 0)
            summonedSlave.Hp = summonedSlave.MaxHp;

        // Move it to target location, and call spawn packet
        summonedSlave.Transform = spawnPos.CloneDetached(summonedSlave);

        // CSSpawnSlave (and any other positionOverride) skips the IsOrigin ground snap above —
        // still pin land vehicles to the owner's floor so client SummonPos Z offsets cannot hover.
        if (owner != null && !summonedSlave.Template.IsABoat() && positionOverride != null)
        {
            var ownerZ = owner.Transform.World.Position.Z;
            var h = World.Template.GeoData.GetHeight(summonedSlave.Transform.World.Position);
            if (h > 0f && MathF.Abs(h - ownerZ) <= 2f)
                summonedSlave.Transform.Local.SetHeight(h);
            else
                summonedSlave.Transform.Local.SetHeight(ownerZ);
        }

        // Retail: CSSpawnSlave.hideSpawnEffect=false (and skill summons, which never send that CS)
        // → UnitState flags bit 11 → client portal_spawn_fx. hideSpawnEffect was previously unused.
        summonedSlave.PendingSpawnPortal = !hideSpawnEffect;
        summonedSlave.Spawn();
        summonedSlave.PendingSpawnPortal = false;

        // Zone owns ship simulation only. Land vehicles announced via WZUnitState get placed against
        // zone-local terrain that does not contain world-space units → Z drift / hover. Boats still
        // need the announce for ServerShipSimulationController.
        AnnounceBoatToZone(summonedSlave);

        if (WorldIntegration.ZoneAuthority && owner != null && summonedSlave.Template.IsABoat())
            WorldIntegration.RelaySlaveMasterChangedToZone?.Invoke(summonedSlave.ObjId, owner.Id, 0);
        #endregion

        // If this was a previously saved slave, load doodads from DB and spawn them
        if (isLoadedPlayerSlave)
        {
            var doodadSpawnCount = World.SpawnManager.SpawnPersistentDoodads(DoodadOwnerType.Slave, (int)summonedSlave.Id, summonedSlave, true);
            Logger.Debug($"Loaded {doodadSpawnCount} doodads from DB for Slave {summonedSlave.ObjId} (Db: {summonedSlave.Id}");
        }

        // Apply equipped gear (used for future parts customization)
        summonedSlave.UpdateGearBonuses(null, null);

        // Ship parts are SlaveEquipment items rather than EquipItem, so the character path above
        // filtered every one of them out and the hull lost their unit_modifiers — a mast is
        // MaxHealth +5000, and the client counts them, so the bar could never read full.
        summonedSlave.UpdateSlaveGearBonuses();
        summonedSlave.Hp = isLoadedPlayerSlave
            ? Math.Min(summonedSlave.Hp, summonedSlave.MaxHp)
            : summonedSlave.MaxHp;

        // Parts that were already on the hull when it was stored never pass through OnEnterContainer, so
        // their item_grade_buffs (sail speed, figurehead skills) have to be re-applied here.
        summonedSlave.UpdateEquipmentBuffs(null, null);

        // Create all remaining doodads that where not previously loaded
        foreach (var doodadBinding in summonedSlave.Template.DoodadBindings)
        {
            // If this AttachPoint has already been spawned, skip its creation
            if (summonedSlave.AttachedDoodads.Any(d => d.AttachPoint == doodadBinding.AttachPointId))
                continue;

            // Create attached doodad
            var doodad = new Doodad
            {
                ObjId = ObjectIdManager.Instance.GetNextId(),
                TemplateId = doodadBinding.DoodadId,
                OwnerObjId = owner?.ObjId ?? 0,
                ParentObjId = summonedSlave.ObjId,
                AttachPoint = doodadBinding.AttachPointId,
                OwnerId = owner?.Id ?? 0,
                PlantTime = summonedSlave.SpawnTime,
                OwnerType = DoodadOwnerType.Slave,
                OwnerDbId = summonedSlave.Id,
                Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId),
                Data = (byte)doodadBinding.AttachPointId, // copy of AttachPointId
                ParentObj = summonedSlave,
                ParentWorld = summonedSlave.ParentWorld, // FIX: Spawn() throws "no owning parent world" without this
                Faction = summonedSlave.Faction,
                Type2 = 1u, // Flag: No idea why it's 1 for slave's doodads, seems to be 0 for everything else
                Spawner = null,
            };

            doodad.SetScale(doodadBinding.Scale);

            doodad.FuncGroupId = doodad.GetFuncGroupId();
            doodad.Transform = summonedSlave.Transform.CloneAttached(doodad);
            doodad.Transform.Parent = summonedSlave.Transform;

            // NOTE: In 1.2 we can't replace slave parts like sail, so just apply it to all the doodads on spawn
            // Should probably have a check somewhere if a doodad can have the UCC applied or not
            if (item != null && item.HasFlag(ItemFlag.HasUCC) && item.UccId > 0)
                doodad.UccId = item.UccId;

            ApplyAttachPointLocation(summonedSlave, doodad, doodadBinding.AttachPointId);

            summonedSlave.AttachedDoodads.Add(doodad);
            doodad.InitDoodad();
            doodad.Spawn();

            // Only set IsPersistent if the binding is defined as such
            if (owner?.Id > 0 && item?.Id > 0 && doodadBinding.Persist)
            {
                doodad.IsPersistent = true;
                doodad.Save();
            }
        }

        // Spawn Slave's slaves
        foreach (var slaveBinding in summonedSlave.Template.SlaveBindings)
        {
            if (slaveBinding.OwnerType != "Slave")
                continue;

            // Child slaves from static slave_bindings (not equipment-driven parts).
            // Equipment parts (sails/cannons) come from item_slave_equipments via SpawnOrReplaceEquipmentVisual.

            var childDbId = 0u;
            var childSlaveName = string.Empty;
            var childSlaveHp = 1;
            var childSlaveMp = 1;
            var childSlaveTemplateId = 0u;
            var isLoadedPlayerChildSlave = false;

            // Only check if the parent was saved as well
            if (summonedSlave.Id > 0)
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();

                // owner_type 2 = BaseUnitType.Slave
                command.CommandText = "SELECT * FROM slaves  WHERE (owner_type = 2) AND (owner_id = @ownerId) AND (summoner = @summoner) AND (attach_point = @attachPoint) LIMIT 1";
                command.Parameters.AddWithValue("@ownerId", summonedSlave.Id);
                command.Parameters.AddWithValue("@summoner", owner?.Id ?? 0);
                command.Parameters.AddWithValue("@attachPoint", slaveBinding.AttachPointId);
                command.Prepare();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    childDbId = reader.GetUInt32("id");
                    childSlaveTemplateId = reader.GetUInt32("template_id");
                    childSlaveName = reader.GetString("name");
                    childSlaveHp = reader.GetInt32("hp");
                    childSlaveMp = reader.GetInt32("mp");
                    isLoadedPlayerChildSlave = true;
                    break;
                }
            } // Parent Slave has DB Id

            if (summonedSlave.Id > 0 && childDbId <= 0)
                childDbId = CharacterIdManager.Instance.GetNextId(); // Slaves of Persistent Slaves are always persistent as well

            var childSlaveTemplate = SlaveGameData.Instance.GetSlaveTemplate(childSlaveTemplateId > 0 ? childSlaveTemplateId : slaveBinding.SlaveId);
            var childTlId = (ushort)TlIdManager.Instance.GetNextId();
            var childObjId = ObjectIdManager.Instance.GetNextId();
            var childSlave = new Slave
            {
                TlId = childTlId,
                ObjId = childObjId,
                ParentObj = summonedSlave,
                TemplateId = childSlaveTemplate.Id,
                Name = string.IsNullOrWhiteSpace(childSlaveName) ? childSlaveTemplate.Name : childSlaveName,
                Level = (byte)childSlaveTemplate.Level,
                ModelId = childSlaveTemplate.ModelId,
                Template = childSlaveTemplate,
                Hp = childSlaveHp,
                Mp = childSlaveMp,
                Faction = summonedSlave.Faction,
                Id = childDbId,
                Summoner = summonedSlave.Summoner,
                SpawnTime = DateTime.UtcNow,
                AttachPointId = (sbyte)slaveBinding.AttachPointId,
                OwnerObjId = summonedSlave.ObjId,
                OwnerType = BaseUnitType.Slave,
                OwnerId = summonedSlave.Id,
            };

            ApplySlaveBonuses(childSlave);

            if (isLoadedPlayerChildSlave)
            {
                childSlave.Hp = Math.Clamp(childSlave.Hp, 0, childSlave.MaxHp);
                childSlave.Mp = Math.Clamp(childSlave.Mp, 0, childSlave.MaxMp);
            }
            else
            {
                childSlave.Hp = childSlave.MaxHp;
                childSlave.Mp = childSlave.MaxMp;
            }

            // Child Slaves will always have their location reset
            childSlave.Transform = summonedSlave.Transform.CloneDetached(childSlave);
            childSlave.Transform.Parent = summonedSlave.Transform;

            ApplyAttachPointLocation(summonedSlave, childSlave, slaveBinding.AttachPointId);

            summonedSlave.AttachedSlaves.Add(childSlave);
            childSlave.Spawn();
            childSlave.PostUpdateCurrentHp(childSlave, 0, childSlave.Hp, KillReason.Unknown);

            // NOTE: This Save is not needed, actual saving will be done by being forwarded from the parent below
            // if (childSlave.Id > 0)
            //     childSlave.Save();
        }

        // Equipment-driven visuals (sails, cannons, cargo, figureheads) from item_slave_equipments.
        SpawnEquipmentVisualsFromInventory(summonedSlave, owner);

        owner?.SendPacket(new SCMySlavePacket(summonedSlave.ObjId, summonedSlave.TlId, summonedSlave.Name,
            summonedSlave.TemplateId,
            summonedSlave.Hp, summonedSlave.MaxHp,
            summonedSlave.Transform.World.Position.X,
            summonedSlave.Transform.World.Position.Y,
            summonedSlave.Transform.World.Position.Z
        ));
        SendUpdatedSlaveSourceItem(owner, summonedSlave);

        // Save to DB
        summonedSlave.Save();

        summonedSlave.PostUpdateCurrentHp(summonedSlave, 0, summonedSlave.Hp, KillReason.Unknown);
        UpdateSlaveRepairPoints(summonedSlave);

        // Dock spheres (Moored / Ezi) may already be active on the owner — push them onto the new hull.
        owner?.Quests?.SyncSphereBuffsToOwnedMounts();

        return summonedSlave;
    }

    /// <summary>
    /// Spawn world meshes for every occupied equipment slot (sails, cannons, cargo, etc.).
    /// </summary>
    private void SpawnEquipmentVisualsFromInventory(Slave hull, Character owner)
    {
        if (hull?.Equipment == null)
            return;

        var spawned = 0;
        for (var slot = 0; slot < hull.Equipment.ContainerSize; slot++)
        {
            var item = hull.Equipment.GetItemBySlot(slot);
            if (item == null)
                continue;
            var beforeSlaves = hull.AttachedSlaves.Count;
            var beforeDoodads = hull.AttachedDoodads.Count;
            SpawnOrReplaceEquipmentVisual(hull, item, (byte)slot, owner);
            if (hull.AttachedSlaves.Count > beforeSlaves || hull.AttachedDoodads.Count > beforeDoodads)
                spawned++;
        }

        Logger.Info(
            "Slave equip visuals spawned={0} for tpl={1} obj={2} (attachedSlaves={3} doodads={4})",
            spawned, hull.TemplateId, hull.ObjId, hull.AttachedSlaves.Count, hull.AttachedDoodads.Count);
    }

    /// <summary>
    /// Retail: equip_slot → slave_equip_slots.attach_point → item_slave_equipments visual
    /// (child SlaveKind=8 or Doodad). Skips attach points owned by static bindings.
    /// </summary>
    public void SpawnOrReplaceEquipmentVisual(Slave hull, Item item, byte equipSlotId, Character owner)
    {
        if (hull?.Template == null)
            return;

        if (!SlaveGameData.Instance.TryGetEquipAttachPoint(hull.TemplateId, equipSlotId, out var attachPoint))
        {
            Logger.Trace("No slave_equip_slots entry for slave={0} slot={1}", hull.TemplateId, equipSlotId);
            return;
        }

        if (SlaveGameData.IsBindingAttachPoint(hull.Template, attachPoint))
        {
            Logger.Debug(
                "Skip equip visual at {0} on slave tpl={1}: attach point is a static binding",
                attachPoint, hull.TemplateId);
            return;
        }

        RemoveEquipmentVisualAtAttachPoint(hull, attachPoint);

        if (item == null)
            return;

        if (!SlaveGameData.Instance.TryResolveEquipVisual(item.TemplateId, item.Grade, out var visual) ||
            visual.IsEmpty)
        {
            Logger.Warn(
                "No item_slave_equipments visual for item={0} grade={1} on slave tpl={2} slot={3}",
                item.TemplateId, item.Grade, hull.TemplateId, equipSlotId);
            return;
        }

        if (visual.SlaveId > 0)
            SpawnEquipmentChildSlave(hull, owner, attachPoint, visual.SlaveId);
        else if (visual.DoodadId > 0)
            SpawnEquipmentDoodad(hull, owner, attachPoint, visual.DoodadId, visual.Scale, item);
    }

    private void RemoveEquipmentVisualAtAttachPoint(Slave hull, AttachPointKind attachPoint)
    {
        // Only remove non-binding children at this attach point.
        if (SlaveGameData.IsBindingAttachPoint(hull.Template, attachPoint))
            return;

        var doodads = hull.AttachedDoodads.Where(d => d.AttachPoint == attachPoint).ToList();
        foreach (var doodad in doodads)
        {
            hull.AttachedDoodads.Remove(doodad);
            doodad.Delete();
        }

        var children = hull.AttachedSlaves.Where(s => (AttachPointKind)s.AttachPointId == attachPoint).ToList();
        foreach (var child in children)
        {
            hull.AttachedSlaves.Remove(child);
            child.Delete();
        }
    }

    private void SpawnEquipmentDoodad(
        Slave hull,
        Character owner,
        AttachPointKind attachPoint,
        uint doodadTemplateId,
        float scale,
        Item sourceItem)
    {
        var doodad = new Doodad
        {
            ObjId = ObjectIdManager.Instance.GetNextId(),
            TemplateId = doodadTemplateId,
            OwnerObjId = owner?.ObjId ?? 0,
            ParentObjId = hull.ObjId,
            AttachPoint = attachPoint,
            OwnerId = owner?.Id ?? 0,
            PlantTime = hull.SpawnTime,
            OwnerType = DoodadOwnerType.Slave,
            OwnerDbId = hull.Id,
            Template = DoodadManager.Instance.GetTemplate(doodadTemplateId),
            Data = (byte)attachPoint,
            ParentObj = hull,
            ParentWorld = hull.ParentWorld,
            Faction = hull.Faction,
            Type2 = 1u,
            Spawner = null,
        };

        if (doodad.Template == null)
        {
            Logger.Warn("Missing doodad template {0} for slave equip visual", doodadTemplateId);
            return;
        }

        doodad.SetScale(scale <= 0f ? 1f : scale);
        doodad.FuncGroupId = doodad.GetFuncGroupId();
        doodad.Transform = hull.Transform.CloneAttached(doodad);
        doodad.Transform.Parent = hull.Transform;

        if (sourceItem is { } item && item.HasFlag(ItemFlag.HasUCC) && item.UccId > 0)
            doodad.UccId = item.UccId;

        ApplyAttachPointLocation(hull, doodad, attachPoint);
        hull.AttachedDoodads.Add(doodad);
        doodad.InitDoodad();
        doodad.Spawn();
    }

    private void SpawnEquipmentChildSlave(
        Slave hull,
        Character owner,
        AttachPointKind attachPoint,
        uint childSlaveTemplateId)
    {
        var childSlaveTemplate = SlaveGameData.Instance.GetSlaveTemplate(childSlaveTemplateId);
        if (childSlaveTemplate == null)
        {
            Logger.Warn("Missing slave template {0} for equip visual", childSlaveTemplateId);
            return;
        }

        var childTlId = (ushort)TlIdManager.Instance.GetNextId();
        var childObjId = ObjectIdManager.Instance.GetNextId();
        var childSlave = new Slave
        {
            TlId = childTlId,
            ObjId = childObjId,
            ParentObj = hull,
            TemplateId = childSlaveTemplate.Id,
            Name = childSlaveTemplate.Name,
            Level = (byte)childSlaveTemplate.Level,
            ModelId = childSlaveTemplate.ModelId,
            Template = childSlaveTemplate,
            Faction = hull.Faction,
            Id = 0,
            Summoner = hull.Summoner,
            SpawnTime = DateTime.UtcNow,
            AttachPointId = (sbyte)attachPoint,
            OwnerObjId = hull.ObjId,
            OwnerType = BaseUnitType.Slave,
            OwnerId = hull.Id,
            ParentWorld = hull.ParentWorld,
        };

        ApplySlaveBonuses(childSlave);
        childSlave.Hp = childSlave.MaxHp;
        childSlave.Mp = childSlave.MaxMp;

        childSlave.Transform = hull.Transform.CloneDetached(childSlave);
        childSlave.Transform.Parent = hull.Transform;
        ApplyAttachPointLocation(hull, childSlave, attachPoint);

        hull.AttachedSlaves.Add(childSlave);
        childSlave.Spawn();
        childSlave.PostUpdateCurrentHp(childSlave, 0, childSlave.Hp, KillReason.Unknown);
    }

    /// <summary>
    /// Use loaded attachPoint location and apply them depending on the slave and point
    /// </summary>
    /// <param name="slave">Owner</param>
    /// <param name="baseUnit">GameObject to apply to</param>
    /// <param name="attachPoint">Location to apply</param>
    private bool ApplyAttachPointLocation(Slave slave, GameObject baseUnit, AttachPointKind attachPoint)
    {
        var attachPoints = SlaveGameData.Instance.GetAttachPointsForSlave(slave.ModelId);
        if (attachPoints != null)
        {
            if (attachPoints.TryGetValue(attachPoint, out var value))
            {
                baseUnit.Transform.Parent = slave.Transform;
                baseUnit.Transform.Local.SetPosition(
                    value.X, value.Y, value.Z,
                    value.Roll, value.Pitch, value.Yaw);
                Logger.Debug($"Model id: {slave.ModelId} attachment {attachPoint} => pos {value} = {baseUnit.Transform}");
                return true;
            }

            Logger.Warn($"Model id: {slave.ModelId} incomplete attach point information");
        }
        else
        {
            Logger.Warn($"Model id: {slave.ModelId} has no attach point information");
        }

        return false;
    }

    /// <summary>
    /// Hands the hull to the dedicate that owns its current zone key and records who has it.
    /// </summary>
    /// <remarks>
    /// Exactly one dedicate may simulate a hull. When a ship summoned in one zone was re-announced in
    /// another (sail out of the summon zone, or re-summon after a zone change) the first dedicate was
    /// never told to drop it, so two of them streamed ShipMoveType for the same bcId: the World mirror
    /// flip-flopped between the two simulations and every client saw the ship snap between two
    /// headings while standing still. Turn skills made it obvious, because WZImpulseUnit is routed by
    /// the hull's current zone and so landed in the dedicate that was not the one steering.
    /// </remarks>
    public static void AnnounceBoatToZone(Slave slave)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template?.IsABoat() != true)
            return;

        var zoneId = slave.Transform?.ZoneId ?? 0;
        if (zoneId == 0)
            return;

        if (slave.ZoneAnnouncedTo != 0 && slave.ZoneAnnouncedTo != zoneId)
            WithdrawBoatFromZone(slave);

        var slaveStateBody = WorldIntegration.BuildWzUnitStateBody(slave);
        if (slaveStateBody is not { Length: > 0 })
            return;

        WorldIntegration.RelayUnitStateToZone?.Invoke(zoneId, slaveStateBody);
        slave.ZoneAnnouncedTo = zoneId;
        Logger.Info("WZUnitState queued for slave obj={0} zoneId={1} bodyLen={2}",
            slave.ObjId, zoneId, slaveStateBody.Length);
    }

    /// <summary>Tells the dedicate that currently simulates this hull to drop it.</summary>
    public static void WithdrawBoatFromZone(Slave slave)
    {
        if (slave == null || slave.ZoneAnnouncedTo == 0)
            return;

        WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(slave.ZoneAnnouncedTo, slave.ObjId);
        Logger.Info("WZUnitRemoved for slave obj={0} zoneId={1}", slave.ObjId, slave.ZoneAnnouncedTo);
        slave.ZoneAnnouncedTo = 0;
    }

    /// <summary>
    /// Applies buff and bonuses to Slave
    /// </summary>
    /// <param name="summonedSlave"></param>
    private static void ApplySlaveBonuses(Slave summonedSlave)
    {
        // Add Passive buffs
        foreach (var buff in summonedSlave.Template.PassiveBuffs)
        {
            var passive = SkillManager.Instance.GetPassiveBuffTemplate(buff.PassiveBuffId);
            summonedSlave.Buffs.AddBuff(passive.BuffId, summonedSlave);
        }

        // Add Normal initial buffs
        foreach (var buff in summonedSlave.Template.InitialBuffs)
            summonedSlave.Buffs.AddBuff(buff.BuffId, summonedSlave);

        // Apply bonuses
        foreach (var bonusTemplate in summonedSlave.Template.Bonuses)
        {
            var bonus = new Bonus
            {
                Template = bonusTemplate,
                Value = bonusTemplate.Value // TODO using LinearLevelBonus
            };
            summonedSlave.AddBonus(0, bonus);
        }
    }

    /// <summary>
    /// Starts task that sends the MySlave packets to players (updates markers on the map)
    /// </summary>
    public void Initialize()
    {
        var sendMySlaveTask = new SendMySlaveTask(World);
        TaskManager.Instance.Schedule(sendMySlaveTask, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Used by SendMySlaveTask
    /// </summary>
    public void SendMySlavePacketToAllOwners()
    {
        var slaves = World.GetAllSlaves();

        foreach (var slave in slaves)
        {
            // The rope's skill_controllers lifetime has to be checked against the clock somewhere; this
            // sweep already walks every slave in the instance, so it carries the expiry.
            ShipHarpoonRopeController.TickHarpoonRopeControllerLifetime(slave);

            // Only the summoned hull has SummoningItem. Child sails/cannons share Summoner and were
            // overwriting the map marker with their own Hp/MaxHp (e.g. 93500/93500 while the hull
            // target bar correctly showed Ezi MaxHp 104500).
            if (slave.Summoner == null || slave.SummoningItem == null || !slave.Template.IsABoat())
                continue;

            var owner = WorldManager.Instance.GetCharacterByObjId(slave.Summoner.ObjId);
            if (owner == null)
                continue;

            owner.SendPacket(new SCMySlavePacket(slave.ObjId, slave.TlId, slave.Name, slave.TemplateId,
                slave.Hp, slave.MaxHp,
                slave.Transform.World.Position.X,
                slave.Transform.World.Position.Y,
                slave.Transform.World.Position.Z));
            SendUpdatedSlaveSourceItem(owner, slave);
        }
    }

    /// <summary>
    /// Retail SC 0x296 — mirror hull HP onto the summon scroll so GetMySlaveHealth stays correct.
    /// </summary>
    public static void SendUpdatedSlaveSourceItem(Character owner, Slave slave)
    {
        if (owner == null || slave?.SummoningItem == null)
            return;

        owner.SendPacket(new SCUpdatedSlaveSourceItemPacket(
            owner.ObjId,
            slave.SummoningItem.Id,
            slave.Hp));
    }

    /// <summary>
    /// Checks if a specified object is mounted on a slave, and returns it's position
    /// </summary>
    /// <param name="objId"></param>
    /// <param name="attachPoint">Attach point the object is on</param>
    /// <returns>Slave the object is on or null of none</returns>
    public Slave GetIsMounted(uint objId, out AttachPointKind attachPoint)
    {
        var slaves = World.GetAllSlaves();
        attachPoint = AttachPointKind.None;
        lock (_slaveListLock)
        {
            foreach (var slave in slaves)
                foreach (var unit in slave.AttachedCharacters)
                {
                    if (unit.Value.ObjId == objId)
                    {
                        attachPoint = unit.Key;
                        return slave;
                    }
                }
        }

        return null;
    }

    /// <summary>
    /// Un-summons a vehicle
    /// </summary>
    /// <param name="character"></param>
    /// <param name="slaveTlId"></param>
    /// <param name="forceDelete">If true, will force delete attached items</param>
    public void RemoveActiveSlave(Character character, ushort slaveTlId, bool forceDelete)
    {
        var slave = GetSlaveByTlId(slaveTlId);
        if (slave != null)
        {
            if (slave.Summoner?.ObjId != character.ObjId)
            {
                Logger.Warn($"Non-owner is trying to desummon a slave {character.Name} => {slave.Name} (ObjId: {slave.ObjId})");
                return;
            }
        }
        else
        {
            return;
        }

        Delete(character, slave.ObjId, forceDelete);
        // slave.Delete();
    }

    /// <summary>
    /// Performs the Rider's Escape action
    /// </summary>
    /// <param name="player"></param>
    /// <param name="skillCastPositionTarget"></param>
    public void RidersEscape(Character player, SkillCastPositionTarget skillCastPositionTarget)
    {
        var mySlave = GetActiveSlaveByOwnerObjId(player.ObjId);
        if (mySlave == null)
        {
            Logger.Warn($"{player.Name} using Rider's Escape with no slave active!");
            return;
        }

        // NOTE: ObjId and TlId gets retained during Rider's Escape

        // Despawn effect
        mySlave.BroadcastPacket(new SCSlaveDespawnPacket(mySlave.ObjId), true);
        mySlave.BroadcastPacket(new SCSlaveRemovedPacket(mySlave.ObjId, mySlave.TlId), true);
        mySlave.SendPacket(new SCUnitsRemovedPacket([mySlave.ObjId]));

        // Move location
        mySlave.SetPosition(skillCastPositionTarget.PosX, skillCastPositionTarget.PosY, skillCastPositionTarget.PosZ, 0f, 0f, skillCastPositionTarget.PosRot);
        // Without this offset, it just doesn't feel right
        mySlave.Transform.Local.AddDistanceToFront(mySlave.Template.SpawnXOffset / 2f);
        mySlave.Transform.Local.AddDistanceToRight(mySlave.Template.SpawnYOffset / 2f);

        // Respawn effect
        mySlave.Hide(); // Hide is needed for it's internals
        mySlave.Spawn();

        if (WorldIntegration.ZoneAuthority)
        {
            // The escape can land the hull in another zone key; move it to that dedicate first so
            // the escape (and everything after it) reaches the process that simulates the ship.
            if (mySlave.ZoneAnnouncedTo != (mySlave.Transform?.ZoneId ?? 0))
                AnnounceBoatToZone(mySlave);

            var p = mySlave.Transform.World.Position;
            WorldIntegration.RelayEscapeSlaveToZone?.Invoke(
                mySlave.ObjId, p.X, p.Y, p.Z, mySlave.Transform.World.Rotation.Z);
            mySlave.BroadcastPacket(new SCEscapeSlavePacket(
                mySlave.ObjId, p.X, p.Y, p.Z, mySlave.Transform.World.Rotation.Z), true);
        }
        //mySlave.SendPacket(new SCUnitStatePacket(mySlave));
        //mySlave.SendPacket(new SCUnitPointsPacket(mySlave.ObjId, mySlave.Hp, mySlave.Mp));
        //mySlave.SendPacket(new SCSlaveStatePacket(mySlave.ObjId, mySlave.TlId, mySlave.Summoner.Name, mySlave.Summoner.ObjId, mySlave.Id));
    }

    /// <summary>
    /// Spawns or de-spawns repairs points on the vehicle based on it's HP
    /// </summary>
    /// <param name="slave"></param>
    public void UpdateSlaveRepairPoints(Slave slave)
    {
        // A slave whose template carries no health reaches this with MaxHp 0 - /testslave does -
        // and the division then throws inside RegenTick, so the exception repeats on every world
        // tick rather than once.
        if (slave.MaxHp <= 0)
            return;

        var hpPercent = slave.Hp * 100 / slave.MaxHp;

        var repairPoints = 0;
        if (hpPercent is < 100 and >= 75)
            repairPoints = slave.Template.Hp75DoodadCount;
        else if (hpPercent is < 75 and >= 50)
            repairPoints = slave.Template.Hp50DoodadCount;
        else if (hpPercent is < 50 and >= 25)
            repairPoints = slave.Template.Hp25DoodadCount;
        else if (hpPercent < 25)
            repairPoints = slave.Template.HealingPointDoodads.Count; // Use max points or Hp 25% ?

        // Get Current Count
        var currentHealPoints = new List<Doodad>();
        var unUsedHealPoints = new List<AttachPointKind>();
        foreach (var healBinding in slave.Template.HealingPointDoodads)
            unUsedHealPoints.Add(healBinding.AttachPointId);

        foreach (var doodad in slave.AttachedDoodads)
        {
            if (doodad.AttachPoint < AttachPointKind.HealPoint0 || doodad.AttachPoint > AttachPointKind.HealPoint9)
                continue;
            currentHealPoints.Add(doodad);
            unUsedHealPoints.Remove(doodad.AttachPoint);
        }

        var pointsToAdd = repairPoints - currentHealPoints.Count;
        if (pointsToAdd < 0)
        {
            // We have too many points, remove some
            for (var iRemove = pointsToAdd; iRemove < 0; iRemove++)
            {
                var i = Random.Shared.Next(currentHealPoints.Count);
                var doodad = currentHealPoints[i];
                if (doodad == null)
                    continue;

                doodad.Hide();
                doodad.Despawn = DateTime.UtcNow;
                World.SpawnManager.AddDespawn(doodad);
                slave.AttachedDoodads.Remove(doodad);
                currentHealPoints.Remove(doodad);
                unUsedHealPoints.Add(doodad.AttachPoint);
                doodad.Delete();
            }
        }

        if (pointsToAdd > 0 && unUsedHealPoints.Count > 0)
        {
            // We don't have enough points, add some
            for (var iAdd = 0; iAdd < pointsToAdd && unUsedHealPoints.Count > 0; iAdd++)
            {
                // pick a random spot
                var wreckPointLocation = unUsedHealPoints[Random.Shared.Next(unUsedHealPoints.Count)];
                unUsedHealPoints.Remove(wreckPointLocation);
                var healBinding = slave.Template.HealingPointDoodads.FirstOrDefault(p => p.AttachPointId == wreckPointLocation);
                if (healBinding == null)
                {
                    Logger.Error($"Somehow failed to grab a healing point {wreckPointLocation} for {slave.TemplateId}");
                    return;
                }

                var wreckArea = new Doodad
                {
                    ObjId = ObjectIdManager.Instance.GetNextId(),
                    TemplateId = healBinding.DoodadId,
                    OwnerObjId = slave.OwnerObjId,
                    ParentObjId = slave.ObjId,
                    AttachPoint = wreckPointLocation,
                    OwnerId = slave.Summoner?.Id ?? 0,
                    PlantTime = DateTime.UtcNow,
                    OwnerType = DoodadOwnerType.Slave,
                    OwnerDbId = slave.Id,
                    Template = DoodadManager.Instance.GetTemplate(healBinding.DoodadId),
                    Data = (byte)wreckPointLocation, // copy of AttachPointId
                    ParentObj = slave,
                    ParentWorld = slave.ParentWorld, // FIX: Spawn() throws "no owning parent world" without this
                    Faction = slave.Faction, // FactionManager.Instance.GetFaction(FactionsEnum.Friendly),
                    Type2 = 1u, // Flag: No idea why it's 1 for slave's doodads, seems to be 0 for everything else
                    Spawner = null,
                    IsPersistent = false,
                };

                wreckArea.SetScale(1f);
                ApplyAttachPointLocation(slave, wreckArea, wreckPointLocation);

                wreckArea.FuncGroupId = wreckArea.GetFuncGroupId();

                slave.AttachedDoodads.Add(wreckArea);
                currentHealPoints.Add(wreckArea);
                wreckArea.Spawn();
            }
        }
    }

    /// <summary>
    /// De-spawns all vehicles owned by the specified player 
    /// </summary>
    /// <param name="owner"></param>
    public void RemoveAndDespawnAllActiveOwnedSlaves(Character owner)
    {
        var activeSlaveInfo = GetActiveSlaveByOwnerObjId(owner.ObjId);
        if (activeSlaveInfo != null)
        {
            activeSlaveInfo.Save();
            Delete(owner, activeSlaveInfo.ObjId, false);
        }
    }

    /// <summary>
    /// RemoveAndDespawnTestSlave - deleting Mirage's test transport
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="slaveObjId"></param>
    /// <returns></returns>
    public void RemoveAndDespawnTestSlave(Character owner, uint slaveObjId)
    {
        Delete(owner, slaveObjId, false);
    }

    /// <summary>
    /// Deleted the slave attached to an Item, deletes it's stored doodads and slaves, and removed them from the DB 
    /// </summary>
    /// <param name="summonSlaveItem"></param>
    /// <returns></returns>
    public bool OnDeleteSlaveItem(SummonSlave summonSlaveItem)
    {
        if (summonSlaveItem.SlaveDbId <= 0)
            return false;

        if (!summonSlaveItem.CanDestroy())
            return false;

        var slaveIdToDelete = summonSlaveItem.SlaveDbId;

        // Despawn the slave if it's currently active
        var currentActiveSlave = GetSlaveByDbId(slaveIdToDelete);
        if (currentActiveSlave != null)
            RemoveActiveSlave(currentActiveSlave.Summoner, currentActiveSlave.TlId, true);

        // Remove the slave from DB
        using var connection = MySQL.CreateConnection();
        if (!DeleteSlaveById(connection, null, slaveIdToDelete))
            return false;

        return true;
    }

    /// <summary>
    /// Deletes a Vehicle from the DB (entry only) 
    /// </summary>
    /// <param name="connection">DB Connection</param>
    /// <param name="transaction">optional transaction</param>
    /// <param name="dbId">Slave DB Id</param>
    /// <returns></returns>
    private bool DeleteSlaveById(MySqlConnection connection, MySqlTransaction transaction, uint dbId)
    {
        using var command = connection.CreateCommand();
        command.Connection = connection;
        if (transaction != null)
            command.Transaction = transaction;
        var deleteCount = 0;

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = $"DELETE FROM slaves WHERE `id` = @removeId";
            deleteCommand.Parameters.AddWithValue("@removeId", dbId);
            deleteCommand.Prepare();
            deleteCount += deleteCommand.ExecuteNonQuery();
        }

        var childDoodadsToRemove = new List<uint>();
        var childSlavesToRemove = new List<uint>();

        // Get list of child doodads to remove
        command.CommandText = "SELECT * FROM doodads WHERE (owner_type = 2) AND (house_id = @ownerId)";
        command.Parameters.AddWithValue("@ownerId", dbId);
        command.Prepare();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                childDoodadsToRemove.Add(reader.GetUInt32("id"));
        }

        // Get a list of child slaves to remove
        command.CommandText = "SELECT * FROM slaves  WHERE (owner_type = 2) AND (owner_id = @ownerId)";
        // command.Parameters.AddWithValue("@ownerId", dbId); // we're recycling the one above
        command.Prepare();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                childSlavesToRemove.Add(reader.GetUInt32("id"));
        }

        // Actually call function to remove
        foreach (var childDoodad in childDoodadsToRemove)
            DoodadManager.Instance.DeleteDoodadById(connection, transaction, childDoodad);

        // Actually call function to remove
        foreach (var childSlaveId in childSlavesToRemove)
            DeleteSlaveById(connection, transaction, childSlaveId);

        if (deleteCount <= 0)
        {
            Logger.Error($"Slave could not be deleted or did not exist, Id {dbId}");
            return false;
        }
        CharacterIdManager.Instance.ReleaseId(dbId);

        return true;
    }
}
