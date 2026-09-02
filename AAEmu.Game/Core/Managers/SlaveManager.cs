using System.Numerics;

using AAEmu.Commons.Network;
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
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Slave;
using AAEmu.Game.Utils;
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
    /// Water surfaces more than this above the caster are treated as bad polygons / sky lakes
    /// (Cinderstone inland hits), not a legal hull spawn.
    /// </summary>
    public const float MaxBoatSurfaceAboveCasterMetres = 8f;

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

    /// <summary>Unit forward alignment of a probe relative to the caster (1 = dead ahead, -1 = behind).</summary>
    public static float BoatSpawnForwardDot(Vector3 caster, float casterYaw, Vector3 probe)
    {
        var fx = -MathF.Sin(casterYaw);
        var fy = MathF.Cos(casterYaw);
        var dx = probe.X - caster.X;
        var dy = probe.Y - caster.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
            return 1f;
        return (dx * fx + dy * fy) / len;
    }

    /// <summary>
    /// Navigable depth under the hull, and reject sky/inland water polygons far above the caster.
    /// </summary>
    public static bool IsBoatSurfaceAllowed(float casterZ, float surfaceZ, float floorZ, float minDepth)
    {
        if (surfaceZ - floorZ <= minDepth)
            return false;
        // High inland hit (floor and surface both above the caster) — not coastal ocean below.
        if (surfaceZ > casterZ + MaxBoatSurfaceAboveCasterMetres && floorZ > casterZ + 2f)
            return false;
        return true;
    }

    /// <summary>Higher is better: prefer ahead of the caster and near the template forward offset.</summary>
    public static float ScoreBoatSpawnCandidate(float forwardDot, float distance, float preferredDistance) =>
        forwardDot * 100f - MathF.Abs(distance - preferredDistance);

    public Slave GetActiveSlaveByOwnerObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            var slaves = World.GetAllSlaves();
            return slaves.FirstOrDefault(slave =>
                slave.Summoner?.ObjId == objId && !slave.IsDead && !slave.IsDespawning);
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
            character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unbond);
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
        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unbond);
        character.AttachedPoint = AttachPointKind.None;

        character.BroadcastPacket(new SCUnitDetachedPacket(character.ObjId, reason), true);
        WorldIntegration.RelayUnitAttachToZone?.Invoke(character.ObjId, slave.ObjId, (byte)attachPoint, false);
        // Helm leave does not send WZShipControlChange control=0. That flag is the zone's ship
        // physics/wave gate: turning it off freezes the hull. Keep sim on until WithdrawBoatFromZone.
    }

    /// <summary>
    /// Mounts a player on a vehicle
    /// </summary>
    /// <param name="character"></param>
    /// <param name="objId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="bondKind"></param>
    public void BindSlave(Character character, uint objId, AttachPointKind attachPoint, AttachUnitReason bondKind, int occupySkillId = 0)
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
            // Hulls hand their simulation to the dedicate that Created them; the switch is scheduled
            // so the seeded pose lands first. Land vehicles are driven by their own client and must
            // not be armed — EnableBoatSimInZone refuses them.
            if (WorldIntegration.ZoneAuthority)
            {
                if (slave.WaterlineSimHeldOff)
                    ResumeHeldBoatSim(slave);
                else
                    EnableBoatSimInZone(slave, slave.ZoneAnnouncedTo);
            }

            if (occupySkillId > 0 || slave.Template.IsABoat())
                SlaveOccupyBuffs.ApplyBuffEffects(character, occupySkillId > 0 ? (uint)occupySkillId : 0, slave);
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

        // Mark before the portal window so a replace-summon does not treat this hull as still active.
        slaveInfo.IsDespawning = true;

        var portalSeconds = Math.Max(0.5f, slaveInfo.Template.PortalTime);

        // Keep sails, figureheads and doodads parented for the portal. Detaching them left them
        // visible in the last cell as standalone units; their ids were then recycled onto the
        // next hull, and crossing back into that cell re-parented the old kit onto the new ship.
        // Persistent flags are cleared so finalize's Delete does not wipe the saved slave row.
        foreach (var doodad in slaveInfo.AttachedDoodads)
        {
            if (owner != null)
                doodad.IsPersistent = false;
        }

        // Client plays slaves.portal_despawn_fx_id when success=true and the unit is still streamed.
        // Withdrawing / hiding in the same tick made the ship vanish instead of sailing into the portal.
        owner?.BroadcastPacket(new SCSlaveDespawnPacket(objId), true);
        owner?.BroadcastPacket(new SCSlaveRemovedPacket(owner.ObjId, slaveInfo.TlId), true);

        // Keep the hull in the World object list and streamed until FinalizeBoatDespawn: removing it
        // here made soft AOI treat the missing id as a leave and send SCUnitsRemoved immediately,
        // which cancelled the portal fx (ship vanished instead of sailing in). Attachments stay
        // on the hull and their object ids stay reserved until that finalize.

        slaveInfo.Despawn = DateTime.UtcNow.AddSeconds(portalSeconds + 0.5f);
        World.SpawnManager.AddDespawn(slaveInfo);

        TaskManager.Instance.Schedule(
            new BoatDespawnFinalizeTask(slaveInfo),
            TimeSpan.FromSeconds(portalSeconds));
    }

    /// <summary>
    /// Zone withdraw of the hull and every attachment, then hide + object-list remove + id
    /// release after the despawn portal has had time to play.
    /// </summary>
    internal static void FinalizeBoatDespawn(Slave slave)
    {
        if (slave == null || slave.DespawnFinalized || !slave.IsDespawning)
            return;

        slave.DespawnFinalized = true;

        WithdrawBoatFromZone(slave);
        TearDownBoatAttachments(slave);

        var world = slave.ParentWorld;
        world?.SpawnManager.CancelDespawn(slave);
        slave.Hide();
        world?.RemoveObject(slave);

        if (slave.ObjId != 0)
        {
            ObjectIdManager.Instance.ReleaseId(slave.ObjId);
            slave.ObjId = 0;
        }

        if (slave.TlId != 0)
        {
            TlIdManager.Instance.ReleaseId(slave.TlId);
            slave.TlId = 0;
        }
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
                // Despawn keeps the object listed until the portal finishes; IsDespawning means it is
                // already out of play for a replace-summon.
                var leftover = GetSlaveByObjId(existingObjId);
                if (leftover != null && !leftover.IsDespawning)
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
        var plantWaterSurfaceZ = float.NaN;

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
            if (owner != null && !slaveTemplate.IsABoat())
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

                // Sweep from the caster's feet — not from an already-advanced "front" point.
                // Advancing first aimed the search inland when facing shore, so the first hit was
                // often behind the player (or a high inland water polygon → hull in the sky).
                var casterLevelPos = owner?.Transform.World.Position ?? spawnPos.World.Position;
                var casterYaw = owner?.Transform.World.Rotation.Z ?? spawnPos.World.Rotation.Z;
                var forwardOffset = Math.Clamp(slaveTemplate.SpawnYOffset, 5f, 50f);
                var worldWaterLevel = GetWaterSurfaceFromAreas(world, casterLevelPos);
                spawnPos.Local.SetHeight(worldWaterLevel);

                // temporary grab ship information so that we can use it to find a suitable spot in front to summon it
                var tempShipModel = ModelManager.Instance.GetShipModel(slaveTemplate.ModelId);
                var minDepth = 5f;
                if (tempShipModel != null)
                    minDepth = tempShipModel.MassBoxSizeZ - tempShipModel.MassCenterZ + 1f;

                // Standalone Game pre-settles from mass-center / keel. ZoneAuthority never does:
                // the dedicate already uses those numbers, and applying Ostera's −1.2 m again
                // planted the boxship half under the water.
                if (tempShipModel != null &&
                    BoatWaterlineRules.ShouldApplyKeelPlant(WorldIntegration.ZoneAuthority))
                {
                    spawnOffsetPos.Z += BoatWaterlineRules.KeelPlantOffset(tempShipModel);
                }

                var searchRange = 50f + (tempShipModel?.MassBoxSizeX ?? 10f);
                var waterAreas = world.Water.GetAreasSnapshot();
                Vector3? bestPos = null;
                var bestScore = float.NegativeInfinity;

                // Two passes: forward hemisphere first, then any heading if the bank faces land.
                for (var pass = 0; pass < 2 && bestPos == null; pass++)
                {
                    var requireForward = pass == 0;
                    for (var distance = forwardOffset; distance <= searchRange; distance += 1f)
                    {
                        for (var step = 0; step < HeadingSweepSteps; step++)
                        {
                            // 0, +15, -15, +30, -30 ... so the caster's own heading still wins ties.
                            var yawOffset = (step + 1) / 2 * HeadingSweepStepRadians * (step % 2 == 0 ? 1f : -1f);
                            var yaw = casterYaw + yawOffset;

                            var probePos = new Vector3(
                                casterLevelPos.X - distance * MathF.Sin(yaw),
                                casterLevelPos.Y + distance * MathF.Cos(yaw),
                                casterLevelPos.Z);

                            var forwardDot = BoatSpawnForwardDot(casterLevelPos, casterYaw, probePos);
                            if (requireForward && forwardDot < 0f)
                                continue;

                            var floorHeight = World.Template.GeoData.GetHeight(probePos);
                            if (floorHeight <= 0f)
                                continue;

                            var surfaceHeight = GetWaterSurfaceFromAreas(world, waterAreas, probePos);
                            if (!IsBoatSurfaceAllowed(casterLevelPos.Z, surfaceHeight, floorHeight, minDepth))
                                continue;

                            var score = ScoreBoatSpawnCandidate(forwardDot, distance, forwardOffset);
                            if (score <= bestScore)
                                continue;

                            bestScore = score;
                            bestPos = new Vector3(probePos.X, probePos.Y, surfaceHeight);
                        }
                    }
                }

                if (bestPos == null)
                {
                    // GetWaterSurface reports the ocean plane for any coordinate, including dry land far
                    // above it, so without this the hull is placed at sea level directly beneath a player
                    // standing inland - buried in the terrain and invisible, with no error to explain it.
                    Logger.Warn(
                        "SlaveSpawn boat template={0} refused: no water at least {1:0.0} deep within {2:0.0}m of ({3:0.0},{4:0.0}); ground {5:0.0}, surface {6:0.0}",
                        slaveTemplate.Id, minDepth, searchRange,
                        casterLevelPos.X, casterLevelPos.Y,
                        World.Template.GeoData.GetHeight(casterLevelPos), worldWaterLevel);
                    owner?.SendErrorMessage(ErrorMessageType.SlaveSpawnErrorInvalidArea);
                    return null;
                }

                plantWaterSurfaceZ = bestPos.Value.Z;
                spawnPos.Local.SetPosition(bestPos.Value.X, bestPos.Value.Y, bestPos.Value.Z);
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
            PlantWaterSurfaceZ = plantWaterSurfaceZ,
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

        // The hull's rig has to be on it before the zone is told about it. A zone derives the hull's
        // speed ceiling, and its health cap, from the attribute values carried by the create it
        // receives, and it is never sent a second one — so a hull announced before its sail was
        // accounted for keeps a bare-model ceiling in that zone for as long as it lives there, while
        // every zone it later crosses into gets a freshly built create and the real figure. That is why
        // a rigged hull could only make its unbuffed speed in the zone it was summoned in.

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
        // their item_grade_buffs (sail speed, figurehead skills) have to be re-applied here. Applying
        // them before the announce is safe: the announce replays every live buff to the zone.
        summonedSlave.UpdateEquipmentBuffs(null, null);
        #endregion

        // If this was a previously saved slave, load doodads from DB and spawn them
        if (isLoadedPlayerSlave)
        {
            var doodadSpawnCount = World.SpawnManager.SpawnPersistentDoodads(DoodadOwnerType.Slave, (int)summonedSlave.Id, summonedSlave, true);
            Logger.Debug($"Loaded {doodadSpawnCount} doodads from DB for Slave {summonedSlave.ObjId} (Db: {summonedSlave.Id}");
        }

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
        // Child-slave Mass is on those templates, not the items. Rebuild after they exist.
        summonedSlave.UpdateSlaveGearBonuses();

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

        // Create the hull after sails and helm doodads exist so the hosting dedicate receives them
        // on the same announce. An earlier create left the zone with a hull and no rig.
        AnnounceBoatToZone(summonedSlave);

        if (WorldIntegration.ZoneAuthority && owner != null && summonedSlave.Template.IsABoat())
            WorldIntegration.RelaySlaveMasterChangedToZone?.Invoke(summonedSlave.ObjId, owner.Id, 0);

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
            if (hull.ZoneAnnouncedTo != 0 && child.ObjId != 0)
                WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(hull.ZoneAnnouncedTo, child.ObjId);
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
        hull.UpdateSlaveGearBonuses();
        AnnounceBoatChildToZone(hull, childSlave);
    }

    /// <summary>
    /// Create then AttachTo one equipment child on the hull's live dedicate.
    /// AttachTo is what puts the child on the model list the mass refresh walks.
    /// </summary>
    private static void AnnounceBoatChildToZone(Slave hull, Slave child)
    {
        if (!WorldIntegration.ZoneAuthority || hull == null || child == null)
            return;
        if (hull.ZoneAnnouncedTo == 0 || child.ObjId == 0 || child.AttachPointId < 0)
            return;

        var zoneKey = hull.ZoneAnnouncedTo;
        var body = WorldIntegration.BuildWzUnitStateBody(child);
        if (body is { Length: > 0 })
        {
            WorldIntegration.RelayUnitStateToZone?.Invoke(zoneKey, child.ObjId, body);
            ReplaySlaveBuffsToZone(child, zoneKey, (int)(hull.Transform?.InstanceId ?? 0));
        }

        WorldIntegration.RelayUnitAttachToZoneId?.Invoke(
            zoneKey, child.ObjId, hull.ObjId, (byte)child.AttachPointId, true);
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
        if (!WorldIntegration.ZoneAuthority || slave?.Template == null)
            return;

        var zoneId = slave.Transform?.ZoneId ?? 0;
        if (zoneId == 0)
            return;

        CommitBoatZoneHandoff(slave, slave.ZoneAnnouncedTo, zoneId);
    }

    /// <summary>
    /// Moves a hull to the zone that will simulate it. Zone A keeps simulating and is what
    /// the client rides; Zone B is Created at A's live pose and armed in the background.
    /// </summary>
    /// <remarks>
    /// Create lands on the new zone first so passengers have a unit to attach to. The outgoing
    /// hull is then removed — <c>WZShipControlChange control=0</c> is never sent; that flag is
    /// the zone's simulation switch, and turning it off is the seam stop. The incoming seed and
    /// helm-on wait for Create to physicalize. A zone with no host cannot take the hull at all,
    /// which ends in <see cref="AbandonBoatWithoutZoneHost"/> rather than a hull nobody simulates.
    /// </remarks>
    public static void CommitBoatZoneHandoff(Slave slave, uint oldZoneKey, uint newZoneKey)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template == null)
            return;

        if (newZoneKey == 0)
            return;

        if (newZoneKey == slave.ZoneAnnouncedTo)
        {
            if (BoatZoneSimRules.ShouldDropStalePending(
                    slave.ZoneSimPendingFor, slave.ZoneAnnouncedTo, newZoneKey))
            {
                DropHullFromZone(slave, slave.ZoneSimPendingFor);
            }

            slave.ZoneSimPendingFor = 0;
            return;
        }

        // Create + helm-on already went to this dedicate; World is still following the previous one
        // until the new body reports. Sending another Create would spawn a second hull there.
        if (newZoneKey == slave.ZoneSimPendingFor)
            return;

        if (!BoatZoneHostGate.HasHost(
                newZoneKey,
                slave.Transform?.InstanceId ?? 0,
                WorldIntegration.IsZoneLoaded,
                WorldIntegration.IsZoneInstanceLoaded))
        {
            if (slave.ZoneAnnouncedTo == 0)
            {
                Logger.Warn(
                    "No zone host for zone {0}; slave obj={1} was not announced", newZoneKey, slave.ObjId);
                return;
            }

            AbandonBoatWithoutZoneHost(slave, newZoneKey);
            return;
        }

        var liveZone = slave.ZoneAnnouncedTo != 0 ? slave.ZoneAnnouncedTo : oldZoneKey;
        if (BoatZoneSimRules.ShouldDropStalePending(slave.ZoneSimPendingFor, liveZone, newZoneKey))
            DropHullFromZone(slave, slave.ZoneSimPendingFor);

        // Snapshot is bookkeeping (epoch / from / to). Do not move the World mirror onto
        // the plant while A is still the streamed body — that snap is the 186→149 jitter.
        var reportAgeMs = CaptureSeamHandoff(
            slave, liveZone, newZoneKey,
            extraAheadMs: (long)BoatZoneSimRules.FirstSummonSimArmDelay.TotalMilliseconds);
        if (slave.SeamHandoff is { } handoff &&
            !BoatZoneSimRules.ShouldOverlapOldSim(liveZone, newZoneKey))
            ApplyHandoffTransform(slave, handoff);
        else
            SyncHullTransformFromZonePose(slave);

        var slaveStateBody = WorldIntegration.BuildWzUnitStateBody(slave);
        if (slave.SeamHandoff == null)
            SyncHullTransformFromZonePose(slave);
        if (slaveStateBody is not { Length: > 0 })
            return;

        WorldIntegration.RelayUnitStateToZone?.Invoke(newZoneKey, slave.ObjId, slaveStateBody);
        if (liveZone == 0)
            slave.ZoneAnnouncedTo = newZoneKey;

        // The new dedicate's ZoneBuffMan only knows buffs from WZBuffCreated packets it received
        // while hosting this unit (its Create handler is the sole writer of that registry, and it
        // silently drops Creates for units it does not know yet). Re-announce everything live on
        // the hull so the incoming simulator applies the same sail/thrust bonuses the old one had.
        ReplaySlaveBuffsToZone(slave, newZoneKey, (int)(slave.Transform?.InstanceId ?? 0));

        // Sails and helm doodads are Created after the hull (parent must exist) and before helm-on.
        AnnounceBoatAttachmentsToZone(slave, newZoneKey);

        // Do not drop A or move riders here. The client stays on A's live type-4 until
        // FinishBoatSeamHandoff. Dropping A at Create was the 1 s stop (frozen plant)
        // and the 186→149 interpolator fight.
        EnableBoatSimInZone(slave, newZoneKey);

        Logger.Info(
            "Boat zone handoff slave obj={0} {1}→{2} bodyLen={3} overlap={4} passengers={5} " +
            "reportAgeMs={6} epoch={7} droppedOldAtTransfer={8}",
            slave.ObjId, liveZone, newZoneKey, slaveStateBody.Length,
            BoatZoneSimRules.ShouldOverlapOldSim(liveZone, newZoneKey),
            slave.AttachedCharacters.Count,
            reportAgeMs,
            slave.SeamHandoffEpoch,
            0);
    }

    /// <summary>
    /// The hull crossed into a zone nothing hosts: stop the old simulation, despawn the hull, and
    /// return everyone aboard to character select.
    /// </summary>
    /// <remarks>
    /// This is the recovery a lost zone connection already performs (see
    /// <see cref="WorldIntegration.NotifyZoneLost"/>). Keeping the hull announced to a zone that does
    /// not exist left it drifting with the pose it had at the seam: the helm did nothing, and the
    /// riders were stuck aboard until they relogged.
    /// </remarks>
    private static void AbandonBoatWithoutZoneHost(Slave slave, uint newZoneKey)
    {
        var riders = slave.AttachedCharacters.Values
            .Where(rider => rider?.Connection != null)
            .Select(rider => (rider.Name, rider.Connection))
            .ToList();

        Logger.Error(
            "No zone host for zone {0}: despawning slave obj={1} and returning {2} rider(s) to character select",
            newZoneKey, slave.ObjId, riders.Count);

        WithdrawBoatFromZone(slave);

        var slaveObjId = slave.ObjId;
        var summoner = slave.Summoner;
        var slaveManager = slave.ParentWorld?.SlaveManager;
        var reason = $"zone {newZoneKey} is not available";

        // Deferred like the on-foot refusal in Character.OnZoneChange: this runs inside the hull's own
        // zone-change callback, which must not tear the hull's object hierarchy down underneath itself.
        _ = Task.Run(() =>
        {
            try
            {
                slaveManager?.Delete(summoner, slaveObjId, true);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not despawn slave obj={0} left in unhosted zone {1}", slaveObjId, newZoneKey);
            }

            foreach (var (name, connection) in riders)
            {
                if (!EnterWorldManager.Instance.ReturnToCharacterSelect(connection, reason))
                    Logger.Warn("Character select was unavailable for {0} aboard slave obj={1}", name, slaveObjId);
            }
        });
    }

    /// <summary>
    /// Re-announces every live buff on the hull to one specific zone instance via
    /// <see cref="WorldIntegration.ReplayBuffCreatedToZone"/>, mirroring the add-time relay's
    /// semantics: non-passive buffs only, zone-authored buffs excluded (the zone made those
    /// itself), unsafe unit references skipped.
    /// </summary>
    private static void ReplaySlaveBuffsToZone(Slave slave, uint newZoneKey, int instanceId)
    {
        ReplayUnitBuffsToZone(slave, newZoneKey, instanceId, "slave");
    }

    private static void ReplayUnitBuffsToZone(Unit unit, uint newZoneKey, int instanceId, string kind)
    {
        if (!WorldIntegration.ZoneAuthority || unit?.Buffs == null)
            return;

        var good = new List<Buff>();
        var bad = new List<Buff>();
        var hidden = new List<Buff>();
        unit.Buffs.GetAllBuffs(good, bad, hidden, false);

        var replayed = 0;
        foreach (var buff in good.Concat(bad).Concat(hidden))
        {
            if (buff.Passive || buff.ZoneAuthored)
                continue;
            if (!BuffCreatedWire.IsZoneSafe(buff, out _))
                continue;

            var body = new PacketStream();
            BuffCreatedWire.Write(body, buff, forZone: true);
            WorldIntegration.ReplayBuffCreatedToZone?.Invoke(newZoneKey, instanceId, unit.ObjId, body.GetBytes());
            buff.RelayedToZone = true;
            replayed++;
        }

        if (replayed > 0)
            Logger.Info(
                "Boat handoff buff replay → zone {0} {1} obj={2} buffs={3}",
                newZoneKey, kind, unit.ObjId, replayed);
    }

    /// <summary>
    /// Creates equipment slaves and attached doodads in the dedicate that just received the hull.
    /// Parent-first so a sail's attach names a unit that already exists.
    /// </summary>
    internal static void AnnounceBoatAttachmentsToZone(Slave hull, uint zoneKey)
    {
        if (!WorldIntegration.ZoneAuthority || hull == null || zoneKey == 0)
            return;

        var children = new List<Slave>();
        var doodads = new List<Doodad>();
        CollectBoatAttachments(hull, children, doodads);
        var instanceId = (int)(hull.Transform?.InstanceId ?? 0);
        var created = new List<(uint ChildObjId, sbyte AttachPoint)>();

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child == null || child.ObjId == 0)
                continue;

            var body = WorldIntegration.BuildWzUnitStateBody(child);
            if (body is not { Length: > 0 })
                continue;

            WorldIntegration.RelayUnitStateToZone?.Invoke(zoneKey, child.ObjId, body);
            ReplaySlaveBuffsToZone(child, zoneKey, instanceId);
            created.Add((child.ObjId, child.AttachPointId));
        }

        foreach (var (childObjId, hullObjId, attachPoint) in BoatAttachmentAnnounceRules.ChildAttachesForZone(
                     hull.ObjId, created))
        {
            WorldIntegration.RelayUnitAttachToZoneId?.Invoke(
                zoneKey, childObjId, hullObjId, attachPoint, true);
        }

        // Attachment doodads stay World-side; see BoatAttachmentAnnounceRules.AnnounceDoodadsToZone.
        var announcedDoodads = 0;
        if (BoatAttachmentAnnounceRules.AnnounceDoodadsToZone)
        {
            foreach (var doodad in doodads)
            {
                if (doodad == null || doodad.ObjId == 0)
                    continue;
                WorldIntegration.RelayCreateDoodadToZoneId?.Invoke(zoneKey, doodad);
                announcedDoodads++;
            }
        }

        if (children.Count > 0 || doodads.Count > 0)
        {
            Logger.Info(
                "Boat attachments announced → zone {0} hull={1} children={2} doodads={3} (withheld {4})",
                zoneKey, hull.ObjId, children.Count, announcedDoodads, doodads.Count - announcedDoodads);
        }
    }

    /// <summary>
    /// Publishes the hull pose to a zone that is about to simulate it. See <see cref="ShipPoseSeed"/>.
    /// </summary>
    public static void PublishHullPoseToZone(Slave slave, uint zoneKey = 0, long extraAheadMs = 0)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template?.IsABoat() != true)
            return;

        var targetZone = zoneKey != 0 ? zoneKey : slave.ZoneAnnouncedTo;
        if (targetZone == 0 || slave.Transform == null)
            return;

        SyncHullTransformFromZonePose(slave);
        var pose = slave.SeamHandoff is { } handoff
            ? ShipPoseSeed.ForHandoff(slave, handoff, ShipPoseSeed.CarryMomentum)
            : ShipPoseSeed.ForSlave(slave, ShipPoseSeed.CarryMomentum, Environment.TickCount64, extraAheadMs);

        // Stamp the destination, not wherever the hull was last reported from. A receiver keeps the zone
        // id of the last ship body it stored and resets its interpolation history the moment an incoming
        // body disagrees with it; seeding the previous zone's id leaves that stored state describing a
        // zone the hull is no longer in, so a later body from the zone actually simulating it reads as a
        // zone change and throws away good interpolation for no reason.
        pose.ZoneId = (ushort)targetZone;

        if (slave.SimulatedShipState is { } report &&
            !ShipPoseSeed.IsReportedMotionReal(slave, report, Environment.TickCount64))
        {
            Logger.Info(
                "Hull seed rest, reported motion uncorroborated obj={0} tpl={1} zone={2} " +
                "reported={3:F2} m/s measured={4:F2} m/s ageMs={5}",
                slave.ObjId, slave.TemplateId, targetZone, report.ReportedSpeed, slave.SimulatedSpeed,
                slave.SimulatedSpeedAtMs == 0 ? -1 : Environment.TickCount64 - slave.SimulatedSpeedAtMs);
        }

        WorldIntegration.RelayMoveToZoneId?.Invoke(targetZone, slave.ObjId, ShipPoseSeed.Build(pose));
    }

    /// <summary>
    /// Turns simulation back on for a hull that was left planted with the helm empty.
    /// </summary>
    private static void ResumeHeldBoatSim(Slave slave)
    {
        if (slave == null || !slave.WaterlineSimHeldOff || slave.ZoneSimEnabledFor == 0)
            return;

        var hasTube = BoatWaterlineRules.HasBuoyancyTube(
            ModelManager.Instance.GetShipModel(slave.ModelId));
        if (!BoatWaterlineRules.ShouldResumeHeldSim(hasTube))
            return;

        var zoneKey = slave.ZoneSimEnabledFor;
        PublishHullPoseToZone(slave, zoneKey);
        WorldIntegration.RelayShipControlChangeToZoneId?.Invoke(zoneKey, slave.ObjId, true);
        ReplayBufferedHelmToZone(slave, zoneKey);
        slave.WaterlineSimHeldOff = false;
        Logger.Info(
            "Ship simulation resumed after waterline hold obj={0} zoneId={1}",
            slave.ObjId, zoneKey);
    }

    /// <summary>
    /// Legacy World waterline step. Only runs while <see cref="Slave.WaterlineSimHeldOff"/>
    /// is still set; ZoneAuthority hulls enable dedicate simulation instead.
    /// </summary>
    public static void TickHeldWaterlineDrive(Slave slave)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template?.IsABoat() != true)
            return;
        if (!slave.WaterlineSimHeldOff || slave.Transform == null)
            return;
        if (slave.AttachedCharacters == null ||
            !slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver))
            return;

        var model = ModelManager.Instance.GetShipModel(slave.ModelId);
        if (model == null || BoatWaterlineRules.HasBuoyancyTube(model))
            return;

        var now = Environment.TickCount64;
        var dt = slave.WaterlineDriveAtMs == 0
            ? BoatWaterlineDriveRules.DefaultStepSeconds
            : (now - slave.WaterlineDriveAtMs) / 1000f;

        var pos = slave.SimulatedShipState;
        var x = pos?.X ?? slave.Transform.World.Position.X;
        var y = pos?.Y ?? slave.Transform.World.Position.Y;
        var z = pos?.Z ?? slave.Transform.World.Position.Z;
        // Transform yaw, not GetSlaveRotationInDegrees on type-4 shorts: those shorts are
        // packed the UseSlaveBase way (quat xyz). The Degrees helper swaps Y/Z and fights
        // the heading every packet.
        var yaw = slave.Transform.World.Rotation.Z;

        var surface = slave.PlantWaterSurfaceZ;
        var world = slave.ParentWorld;
        if (world?.Water != null)
            surface = GetWaterSurfaceFromAreas(world, new Vector3(x, y, z));
        if (float.IsNaN(surface))
            surface = z;

        var cruise = BoatWaterlineDriveRules.CruiseSpeed(
            slave.ThrottleRequest, model.Velocity, model.ReverseVelocity);
        var (nx, ny, nz, nextYaw, velX, velY) = BoatWaterlineDriveRules.Step(
            x, y, surface, yaw,
            slave.ThrottleRequest, slave.SteeringRequest,
            cruise,
            model.SteerVel,
            dt);

        var pose = ShipPoseSeed.ForWaterlineRecover(slave, nx, ny, nz);
        var (rotX, rotY, rotZ) = BoatWaterlineDriveRules.RotationShortsFromYaw(nextYaw);
        pose.RotationX = rotX;
        pose.RotationY = rotY;
        pose.RotationZ = rotZ;
        pose.VelX = BoatSeamHandoffRules.EncodeVelMetresPerSecond(velX);
        pose.VelY = BoatSeamHandoffRules.EncodeVelMetresPerSecond(velY);
        pose.VelZ = 0;
        pose.Throttle = slave.ThrottleRequest;
        pose.Steering = slave.SteeringRequest;
        var zoneKey = slave.ZoneSimEnabledFor != 0 ? slave.ZoneSimEnabledFor : slave.ZoneAnnouncedTo;
        if (zoneKey != 0)
            pose.ZoneId = (ushort)zoneKey;

        ApplySeamAuthorityPose(slave, pose);
        // Apply decodes shorts with GetSlaveRotationInDegrees. Keep Transform yaw as the
        // step we just integrated so the next packet does not fight the heading.
        slave.Transform.Local.SetPosition(nx, ny, nz, 0f, 0f, nextYaw);
        slave.Transform.FinalizeTransform();
        slave.WaterlineDriveAtMs = now;
        if (zoneKey != 0)
            WorldIntegration.RelayMoveToZoneId?.Invoke(zoneKey, slave.ObjId, ShipPoseSeed.Build(pose));
        slave.BroadcastPacket(new SCOneUnitMovementPacket(slave.ObjId, pose), false);
    }

    /// <summary>
    /// Type-4 of the pose A is streaming right now. Never the seam snapshot.
    /// </summary>
    public static void PublishLiveHullPoseToZone(Slave slave, uint zoneKey, bool carryMomentum = true)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template?.IsABoat() != true)
            return;
        if (zoneKey == 0 || slave.Transform == null)
            return;

        var pose = ShipPoseSeed.ForLiveReport(slave, carryMomentum);
        pose.ZoneId = (ushort)zoneKey;
        WorldIntegration.RelayMoveToZoneId?.Invoke(zoneKey, slave.ObjId, ShipPoseSeed.Build(pose));
    }

    /// <summary>
    /// After helm-on the zone ignores further type-4. World does not restomp sim off/on
    /// to plant the waterline — that cycle rebuilt the PE on prefab-buoy hulls. Zone
    /// sim is left on; <see cref="BoatWaterlineRules.ShouldRecover"/> is always false.
    /// </summary>
    public static void TryRecoverBoatWaterline(Slave slave)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template?.IsABoat() != true)
            return;
        if (slave.IsDespawning || slave.Transform == null)
            return;
        if (slave.ZoneSimEnabledFor == 0 || slave.ZoneSimPendingFor != 0)
            return;

        var pos = slave.SimulatedShipState;
        var hullX = pos?.X ?? slave.Transform.World.Position.X;
        var hullY = pos?.Y ?? slave.Transform.World.Position.Y;
        var hullZ = pos?.Z ?? slave.Transform.World.Position.Z;

        var surface = slave.PlantWaterSurfaceZ;
        var world = slave.ParentWorld;
        if (world?.Water != null)
            surface = GetWaterSurfaceFromAreas(world, new Vector3(hullX, hullY, hullZ));
        if (float.IsNaN(surface))
            return;

        var model = ModelManager.Instance.GetShipModel(slave.ModelId);
        var hasTube = BoatWaterlineRules.HasBuoyancyTube(model);
        var now = Environment.TickCount64;
        var armedAgeMs = slave.SeamArmedAtMs == 0 ? -1 : now - slave.SeamArmedAtMs;
        var recoverAgeMs = slave.WaterlineRecoverAtMs == 0 ? -1 : now - slave.WaterlineRecoverAtMs;
        var hasDriver = slave.AttachedCharacters != null &&
                        slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver);
        var throttle = pos?.Throttle ?? slave.Throttle;

        if (!BoatWaterlineRules.ShouldRecover(
                slave.SeamHandoff != null,
                armedAgeMs,
                recoverAgeMs,
                surface,
                hullZ,
                slave.SimulatedSpeed,
                throttle,
                hasDriver,
                hasTube))
            return;

        var targetZ = BoatWaterlineRules.RecoverZ(surface, hullZ);
        var pose = ShipPoseSeed.ForWaterlineRecover(slave, hullX, hullY, targetZ);
        pose.ZoneId = (ushort)slave.ZoneSimEnabledFor;
        var sog = slave.SimulatedSpeed;
        var holdOff = BoatWaterlineRules.ShouldHoldSimOff(hasTube, hasDriver);

        WorldIntegration.RelayShipControlChangeToZoneId?.Invoke(slave.ZoneSimEnabledFor, slave.ObjId, false);
        WorldIntegration.RelayMoveToZoneId?.Invoke(slave.ZoneSimEnabledFor, slave.ObjId, ShipPoseSeed.Build(pose));
        if (!holdOff)
            WorldIntegration.RelayShipControlChangeToZoneId?.Invoke(slave.ZoneSimEnabledFor, slave.ObjId, true);
        ApplySeamAuthorityPose(slave, pose);
        slave.WaterlineRecoverAtMs = now;
        slave.WaterlineSimHeldOff = holdOff;
        slave.SimulatedSpeed = 0f;

        Logger.Info(
            "Boat waterline recover obj={0} tpl={1} zone={2} ({3:0.0},{4:0.0},{5:0.0}) → Z={6:0.0} " +
            "surface={7:0.0} holdOff={8} sog={9:0.0}",
            slave.ObjId, slave.TemplateId, slave.ZoneSimEnabledFor,
            hullX, hullY, hullZ, targetZ, surface, holdOff, sog);
    }

    /// <summary>
    /// While B has not taken the helm, keep its un-simulated body on A's live pose.
    /// Type-4 is ignored after helm-on.
    /// </summary>
    public static void TrackIncomingSeam(Slave slave)
    {
        if (slave?.SeamHandoff == null || slave.ZoneSimPendingFor == 0 || slave.SeamReplantAtMs != 0)
            return;

        PublishLiveHullPoseToZone(slave, slave.ZoneSimPendingFor);
    }

    /// <summary>
    /// Finish the overlap when A has gone silent, after any just-fired impulse has
    /// had time to land. Called from A's live type-4 so a mute B still hands off.
    /// Do not time-out onto a short B while A is still talking — that was the
    /// reverse 8.8 → 4.8 hitch.
    /// </summary>
    public static void TickSeamOverlap(Slave slave)
    {
        if (slave?.SeamHandoff == null || slave.ZoneSimPendingFor == 0 || slave.SeamReplantAtMs == 0)
            return;
        if (!BoatZoneSimRules.ShouldOverlapOldSim(slave.ZoneAnnouncedTo, slave.ZoneSimPendingFor))
            return;

        var now = Environment.TickCount64;
        var impulseSettling = slave.SeamImpulseAtMs != 0 &&
            now - slave.SeamImpulseAtMs < BoatZoneSimRules.ImpulseSettleMs;
        if (!impulseSettling && IsOldSimSilent(slave, now))
        {
            FinishBoatSeamHandoff(slave);
        }
    }

    private static bool IsOldSimSilent(Slave slave, long nowMs) =>
        slave.SimulatedShipStateAtMs == 0 ||
        nowMs - slave.SimulatedShipStateAtMs >= BoatZoneSimRules.OldSimSilentMs;

    /// <summary>
    /// Freezes the outgoing simulator's last report as the only snapshot this handoff may advance.
    /// </summary>
    private static long CaptureSeamHandoff(Slave slave, uint fromZone, uint toZone, long extraAheadMs)
    {
        slave.SeamHandoffEpoch++;
        slave.SeamImpulseAtMs = 0;
        slave.SeamBridgeBehindAtMs = 0;
        var liveThrottle = BoatSeamPredictRules.LiveThrottle(
            slave.SimulatedShipState?.Throttle ?? 0, slave.ThrottleRequest, slave.Throttle);
        if (!BoatSeamHandoffRules.TryCapture(
                slave.SimulatedShipState,
                slave.SimulatedShipStateAtMs,
                slave.PreviousSimulatedShipState,
                slave.PreviousSimulatedShipStateAtMs,
                slave.SeamHandoffEpoch,
                fromZone,
                toZone,
                Environment.TickCount64,
                extraAheadMs,
                liveThrottle,
                out var snapshot))
        {
            slave.SeamHandoff = null;
            return extraAheadMs;
        }

        slave.SeamHandoff = snapshot;
        slave.SeamHelmQueue.Clear();
        slave.SeamReplantAtMs = 0;
        return BoatSeamHandoffRules.DeltaMs(snapshot);
    }

    private static void ApplyHandoffTransform(Slave slave, in BoatSeamHandoffSnapshot snapshot)
    {
        if (slave.Transform == null)
            return;

        var (x, y, z, _, _, _) = BoatSeamHandoffRules.Propagate(snapshot);
        var (rotShortX, rotShortY, rotShortZ) = BoatSeamHandoffRules.PropagateRotation(snapshot);
        var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationInDegrees(rotShortX, rotShortY, rotShortZ);
        slave.Transform.Local.SetPosition(x, y, z, rotX, rotY, rotZ);
        slave.Transform.FinalizeTransform();
    }

    /// <summary>
    /// Helm received while a seam is in flight. After the incoming zone is armed, send it there
    /// as well as to the followed zone, so the new body tracks the stick before follow switches.
    /// </summary>
    public const int SeamHelmQueueCap = 32;

    public static void NoteSeamHelm(Slave slave)
    {
        if (slave?.SeamHandoff == null)
            return;

        var throttle = BoatSeamPredictRules.LiveThrottle(0, slave.ThrottleRequest, slave.Throttle);
        var steering = slave.SteeringRequest != 0 ? slave.SteeringRequest : slave.Steering;
        var sample = new BoatSeamHelmSample(throttle, steering);
        if (slave.SeamHelmQueue.Count == 0 || slave.SeamHelmQueue[^1] != sample)
        {
            if (slave.SeamHelmQueue.Count >= SeamHelmQueueCap)
                slave.SeamHelmQueue.RemoveAt(0);
            slave.SeamHelmQueue.Add(sample);
        }

        // Transform.ZoneId already flipped, so ForUnit sends the stick to B.
        // A is still the streamed body and must keep the same throttle.
        if (slave.ZoneAnnouncedTo != 0)
            SendHelmToZone(slave, slave.ZoneAnnouncedTo, sample);

        if (slave.ZoneSimPendingFor == 0 ||
            slave.ZoneSimEnabledFor != slave.ZoneSimPendingFor ||
            slave.ZoneSimPendingFor == slave.ZoneAnnouncedTo)
            return;

        SendHelmToZone(slave, slave.ZoneSimPendingFor, sample);
    }

    /// <summary>
    /// Helm held during the overlap went to the followed (old) zone. Replay it onto the incoming
    /// simulator once World starts following, so a change in that window is not lost.
    /// </summary>
    private static void ReplayBufferedHelmToZone(Slave slave, uint zoneKey)
    {
        if (slave.SeamHelmQueue.Count > 0)
        {
            foreach (var sample in slave.SeamHelmQueue)
                SendHelmToZone(slave, zoneKey, sample);
            return;
        }

        var throttle = BoatSeamPredictRules.LiveThrottle(0, slave.ThrottleRequest, slave.Throttle);
        var steering = slave.SteeringRequest != 0 ? slave.SteeringRequest : slave.Steering;
        SendHelmToZone(slave, zoneKey, new BoatSeamHelmSample(throttle, steering));
    }

    private static void SendHelmToZone(Slave slave, uint zoneKey, BoatSeamHelmSample sample)
    {
        if (sample.Throttle == 0 && sample.Steering == 0)
            return;

        var request = new ShipRequestMoveType
        {
            Type = MoveTypeEnum.ShipRequest,
            Time = (uint)Math.Max(0, (DateTime.UtcNow - slave.SpawnTime).TotalMilliseconds),
            Throttle = sample.Throttle,
            Steering = sample.Steering
        };
        var stream = new PacketStream();
        stream.Write((byte)MoveTypeEnum.ShipRequest);
        request.Write(stream);
        WorldIntegration.RelayMoveToZoneId?.Invoke(zoneKey, slave.ObjId, stream.GetBytes());
        Logger.Info(
            "Boat seam helm replay → zone {0} slave obj={1} throttle={2} steering={3}",
            zoneKey, slave.ObjId, sample.Throttle, sample.Steering);
    }

    /// <summary>
    /// Puts the World mirror, internal movement state, and helm on the same adjusted pose.
    /// Passengers and attachments follow the hull parent; this is not a zone measurement.
    /// </summary>
    public static void ApplySeamAuthorityPose(Slave slave, ShipMoveType pose)
    {
        if (slave?.Transform == null || pose == null)
            return;

        var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationInDegrees(
            pose.RotationX, pose.RotationY, pose.RotationZ);
        slave.Transform.Local.SetPosition(pose.X, pose.Y, pose.Z, rotX, rotY, rotZ);
        slave.Transform.FinalizeTransform();
        slave.Throttle = pose.Throttle;
        slave.Steering = pose.Steering;
        slave.SimulatedShipState = pose;
        slave.SimulatedShipStateAtMs = Environment.TickCount64;
    }

    /// <summary>
    /// Puts the World mirror on the snapshot evaluated at now, without treating a zone report as
    /// a measurement.
    /// </summary>
    public static void ApplySeamBridgeTransform(Slave slave, ShipMoveType pose) =>
        ApplySeamAuthorityPose(slave, pose);

    /// <summary>
    /// Extra plant beyond the physicalize wait. Zero: follow switches on the first consumed
    /// report, so a second of overlap-ahead is a future xyz the new body then crawls from.
    /// </summary>
    private static long SeamOverlapAheadMs(Slave slave, bool seamOverlap)
    {
        var now = Environment.TickCount64;
        var reportedThrottle = slave.SimulatedShipState?.Throttle ?? 0;
        var speed = slave.SimulatedShipState?.ReportedSpeed ?? 0f;
        if (speed <= 0f)
            speed = slave.SimulatedSpeed;
        var speedAgeMs = slave.SimulatedSpeedAtMs == 0
            ? long.MaxValue
            : now - slave.SimulatedSpeedAtMs;
        return BoatSeamPredictRules.OverlapAheadMs(
            seamOverlap,
            speed,
            speedAgeMs,
            BoatSeamPredictRules.LiveThrottle(reportedThrottle, slave.ThrottleRequest, slave.Throttle));
    }

    /// <summary>
    /// Pulls World transform onto the last zone-reported pose, advanced by the way that pose was
    /// making, so Create is not announced at the dock or at a late seam snapshot.
    /// </summary>
    private static void SyncHullTransformFromZonePose(Slave slave, long extraAheadMs = 0)
    {
        if (slave.SimulatedShipState is not { } last || slave.Transform == null)
            return;

        var ageMs = slave.SimulatedShipStateAtMs == 0
            ? 0
            : Environment.TickCount64 - slave.SimulatedShipStateAtMs;
        var (x, y, z) = BoatSeamPredictRules.Advance(
            last.X, last.Y, last.Z, last.VelX, last.VelY, last.VelZ,
            BoatSeamPredictRules.AheadMs(ageMs, extraAheadMs));
        var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationInDegrees(last.RotationX, last.RotationY, last.RotationZ);
        slave.Transform.Local.SetPosition(x, y, z, rotX, rotY, rotZ);
        slave.Transform.FinalizeTransform();
        slave.Throttle = last.Throttle;
        slave.Steering = last.Steering;
    }

    /// <summary>
    /// Hands ship simulation for this hull to a zone, once per zone. The previous dedicate is left
    /// running until <see cref="CommitBoatSimEnable"/> switches World over and drops it.
    /// Seed and helm-on wait one Create-physicalize delay so the type-4 pose has a body.
    /// </summary>
    /// <remarks>
    /// <c>ShipControlChange</c> is the zone's simulation switch for a hull, not a "someone is at the
    /// wheel" notification. Re-sending it on every helm mount re-entered the simulation and froze or
    /// launched the hull. Create does not place the rigid body, so seed and helm-on wait
    /// <see cref="BoatZoneSimRules.FirstSummonSimArmDelay"/> after Create; type-4 is then sent
    /// immediately before helm-on. After helm-on the zone drops further type-4. World keeps
    /// following the previous simulator until the new one publishes a placed pose; that pose is
    /// not streamed. Switching the follow on arm put an unplaced or rest pose on the wire.
    /// </remarks>
    public static void EnableBoatSimInZone(Slave slave, uint zoneKey)
    {
        if (!WorldIntegration.ZoneAuthority || slave?.Template == null)
            return;

        // Hulls only. The switch is dispatched by the zone against whatever model class the unit has,
        // and a land vehicle's simulator has no steering or throttle input at all — it holds the
        // handbrake and drives both to zero, and while it is armed the zone discards the movement
        // World relays for that unit. Arming a cart therefore parks it and deafens it to its driver.
        // Land vehicles stay client-driven (see SlaveTemplate.IsClientDrivenLandVehicle).
        if (!slave.Template.IsZoneSimulatedHull())
            return;

        if (zoneKey == 0 || slave.ZoneSimPendingFor == zoneKey)
            return;

        // Helm mount passes the zone World is following. During a seam that is still the old
        // dedicate; re-arming it would overwrite the pending new zone.
        if (BoatZoneSimRules.IsWarmupSource(
                slave.ZoneSimEnabledFor, slave.ZoneAnnouncedTo, slave.ZoneSimPendingFor) &&
            zoneKey == slave.ZoneAnnouncedTo)
            return;

        if (!BoatZoneSimRules.ShouldArm(zoneKey, slave.ZoneSimEnabledFor) && slave.ZoneSimPendingFor == 0)
            return;

        slave.ZoneSimPendingFor = zoneKey;
        if (BoatZoneSimRules.ShouldDeferSimArm(slave.ZoneAnnouncedTo, zoneKey) &&
            TaskManager.Instance.Schedule(
                new BoatZoneSimEnableTask(slave, zoneKey),
                BoatZoneSimRules.FirstSummonSimArmDelay))
        {
            Logger.Info(
                "Ship simulation arm deferred obj={0} zoneId={1} delayMs={2}",
                slave.ObjId, zoneKey, BoatZoneSimRules.FirstSummonSimArmDelay.TotalMilliseconds);
            return;
        }

        CommitBoatSimEnable(slave, zoneKey);
    }

    /// <summary>
    /// Speed samples reported after a seam. Enough to show whether the hull held its way or rebuilt it
    /// from rest, without following it for the rest of the voyage.
    /// </summary>
    public const int SeamSpeedProbeCount = 8;

    /// <summary>
    /// Arms the new dedicate. On a seam A keeps simulating and is streamed; B is seeded at
    /// A's live pose (carry + open-loop shortfall) and follow waits for B's cruise (or A
    /// silent after the impulse lands, or the overlap fail-safe). First summon has no
    /// previous simulator.
    /// </summary>
    internal static void CommitBoatSimEnable(Slave slave, uint zoneKey)
    {
        if (slave == null ||
            !BoatZoneSimRules.ShouldSendEnable(zoneKey, slave.ZoneAnnouncedTo, slave.ZoneSimPendingFor))
        {
            return;
        }

        var liveZone = slave.ZoneAnnouncedTo;
        var now = Environment.TickCount64;
        var overlap = slave.SeamHandoff != null &&
                      BoatZoneSimRules.ShouldOverlapOldSim(liveZone, zoneKey);

        if (slave.SeamHandoff is { } snap)
        {
            // Create already used the planned activation. A later `now` is scheduler
            // slack, not a second kinematic advance (that is the stale-entry rollback).
            var plantAt = BoatSeamHandoffRules.PlannedActivationTick(snap, now);
            if (!BoatSeamHandoffRules.TryBindActivationInDestinationZone(
                    snap, plantAt, (x, y) => ZoneKeyAt(slave, x, y), out var bound))
            {
                Logger.Warn(
                    "Seam projection left destination obj={0} {1}→{2}; planting the transfer pose",
                    slave.ObjId, snap.FromZone, snap.ToZone);
            }

            slave.SeamHandoff = bound;
            // Overlap: the World mirror and SimulatedShipState stay on A's live report.
            // Applying the plant here is the frozen-xyz hitch.
            if (!overlap)
                ApplySeamAuthorityPose(slave, ShipPoseSeed.ForHandoff(slave, bound, ShipPoseSeed.CarryMomentum));
        }

        if (overlap)
            PublishLiveHullPoseToZone(slave, zoneKey, carryMomentum: true);
        else
            PublishHullPoseToZone(slave, zoneKey);

        var hasDriver = slave.AttachedCharacters != null &&
                        slave.AttachedCharacters.ContainsKey(AttachPointKind.Driver);
        var holdOff = BoatWaterlineRules.ShouldHoldSimOff(
            BoatWaterlineRules.HasBuoyancyTube(ModelManager.Instance.GetShipModel(slave.ModelId)),
            hasDriver);
        if (!holdOff)
            WorldIntegration.RelayShipControlChangeToZoneId?.Invoke(zoneKey, slave.ObjId, true);
        slave.WaterlineSimHeldOff = holdOff;
        ReplayBufferedHelmToZone(slave, zoneKey);
        ArmSeamSpeedCorrection(slave, zoneKey);
        if (overlap)
        {
            ApplySeamOpenLoopRestore(slave, zoneKey);
            slave.SeamReplantAtMs = now;
        }

        slave.ZoneSimEnabledFor = zoneKey;
        slave.SeamArmedAtMs = now;

        Logger.Info(
            "Seam speed handoff obj={0} zone={1}→{2} carriedBySeed={3} measuredBefore={4:F1} m/s " +
            "reportedBefore={5:F1} m/s cap={6:F1} m/s throttle={7} handoffMs={8}",
            slave.ObjId, liveZone, zoneKey, ShipPoseSeed.CarryMomentum, slave.SimulatedSpeed,
            slave.SimulatedShipState?.ReportedSpeed ?? 0f,
            ShipPoseSeed.EffectiveMaxVelocity(slave), slave.Throttle,
            slave.SeamHandoff is { } planted ? BoatSeamHandoffRules.DeltaMs(planted) : 0);

        if (slave.SeamHandoff != null)
        {
            Logger.Info(
                "Ship simulation armed for slave obj={0} zoneId={1} clientBridge={2} overlap={3}",
                slave.ObjId, zoneKey, overlap ? 0 : 1, overlap);
            return;
        }

        slave.ZoneAnnouncedTo = zoneKey;
        slave.ZoneSimPendingFor = 0;
        Logger.Info(
            "Ship simulation enabled for slave obj={0} zoneId={1} droppedOld=0 holdOff={2}",
            slave.ObjId, zoneKey, holdOff);
        slave.SeamImpulseAtMs = 0;
        slave.SeamBridgeBehindAtMs = 0;
        slave.SeamHelmQueue.Clear();
        slave.SeamSpeedProbes = SeamSpeedProbeCount;
    }

    /// <summary>
    /// Hands riders to the incoming zone and removes the outgoing unit. Used at
    /// <see cref="FinishBoatSeamHandoff"/>, not at Create.
    /// </summary>
    private static bool TakeSeamOwnership(Slave slave, uint fromZone, uint toZone, bool replayHelm)
    {
        if (fromZone == 0 || fromZone == toZone)
        {
            slave.ZoneAnnouncedTo = toZone;
            return true;
        }

        if (!HandoffPassengersToZone(slave, fromZone, toZone))
        {
            DropHullFromZone(slave, fromZone);
            AbandonBoatWithoutZoneHost(slave, toZone);
            return false;
        }

        DropHullFromZone(slave, fromZone, detachPassengers: false);
        slave.ZoneAnnouncedTo = toZone;
        RefreshClientSeatBinds(slave);
        if (replayHelm)
            ReplayBufferedHelmToZone(slave, toZone);
        return true;
    }

    private static uint ZoneKeyAt(Slave slave, float x, float y)
    {
        var template = slave.ParentWorld?.Template;
        if (template == null)
            return 0;

        var sx = (int)(x / WorldManager.REGION_SIZE);
        var sy = (int)(y / WorldManager.REGION_SIZE);
        if (!template.ValidRegion(sx, sy))
            return 0;

        return WorldManager.Instance.GetZoneId(template, x, y);
    }

    /// <summary>
    /// Incoming-zone reports World is not following yet. A consumed-but-slow pose gets the
    /// closed-loop impulse and is not streamed. Follow switches when that body publishes the
    /// restored cruise <em>and</em> has reached the bridged plant — cruise at the Create xyz
    /// is still behind the pose the client is looking at.
    /// </summary>
    public static void ObserveSeamWarmupPose(Slave slave, uint zoneKey, float reportedSpeed, float x, float y)
    {
        if (slave == null)
            return;

        if (slave.ZoneSimEnabledFor != zoneKey || slave.SeamArmedAtMs == 0)
            return;

        if (slave.SeamHandoff is { } snap &&
            !BoatSeamHandoffRules.IsForActivation(snap, zoneKey, slave.SeamHandoffEpoch))
        {
            Logger.Info(
                "Seam warmup ignored stale handoff obj={0} zone={1} epoch={2} snapZone={3} snapEpoch={4}",
                slave.ObjId, zoneKey, slave.SeamHandoffEpoch, snap.ToZone, snap.Epoch);
            return;
        }

        var now = Environment.TickCount64;
        var elapsedMs = now - slave.SeamArmedAtMs;

        // A is still the streamed body. B warms in the background: closed-loop
        // shortfall may fire here, but B is not streamed and follow waits for cruise.
        if (TryFinishOverlapSeam(slave, zoneKey, reportedSpeed, x, y, now))
            return;

        var snapshotSpeed = slave.SeamHandoff is { } live
            ? BoatSeamHandoffRules.LinearSpeed(live)
            : 0f;
        var liveThrottle = BoatSeamPredictRules.LiveThrottle(
            0, slave.ThrottleRequest, slave.Throttle);
        var expectedCruise = BoatZoneSimRules.ExpectedCruiseForWarmup(
            slave.SeamTargetSpeed, snapshotSpeed, liveThrottle);

        var msSinceImpulse = slave.SeamImpulseAtMs == 0 ? long.MaxValue : now - slave.SeamImpulseAtMs;
        if (BoatZoneSimRules.ShouldImpulseWarmup(x, y, reportedSpeed, expectedCruise, elapsedMs) &&
            (slave.SeamImpulseAtMs == 0 || msSinceImpulse >= BoatZoneSimRules.ImpulseSettleMs))
        {
            slave.SeamTargetSpeed = expectedCruise;
            slave.SeamCorrectionZone = zoneKey;
            ApplySeamSpeedCorrection(slave, zoneKey, reportedSpeed);
            slave.SeamImpulseAtMs = now;
            Logger.Info(
                "Seam impulse sent, client still on snapshot obj={0} zone={1} arrived={2:F1} " +
                "expectedCruise={3:F1} elapsedMs={4}",
                slave.ObjId, zoneKey, reportedSpeed, expectedCruise, elapsedMs);
            return;
        }

        var sinceImpulse = slave.SeamImpulseAtMs == 0 ? -1 : now - slave.SeamImpulseAtMs;
        if (!BoatZoneSimRules.ShouldAcceptWarmupHandoff(
                x, y, reportedSpeed, expectedCruise, elapsedMs, sinceImpulse))
        {
            Logger.Info(
                "Seam warmup pose ignored obj={0} zone={1} pos=({2:F1},{3:F1}) reportedVel={4:F1} " +
                "expectedCruise={5:F1} elapsedMs={6} msSinceImpulse={7}",
                slave.ObjId, zoneKey, x, y, reportedSpeed, expectedCruise, elapsedMs,
                sinceImpulse);
            return;
        }

        if (slave.SeamHandoff is { } planted &&
            !BoatSeamHandoffRules.HasReachedClientBridge(planted, x, y, now))
        {
            if (slave.SeamBridgeBehindAtMs == 0)
                slave.SeamBridgeBehindAtMs = now;

            var at = BoatSeamHandoffRules.ClientBridgeTick(planted, now);
            var (bridgeX, bridgeY, _, _, _, _) = BoatSeamHandoffRules.EvaluateAt(planted, at);
            var behindMs = now - slave.SeamBridgeBehindAtMs;
            Logger.Info(
                "Seam warmup pose behind bridge obj={0} zone={1} pos=({2:F1},{3:F1}) " +
                "bridge=({4:F1},{5:F1}) along={6:F1} m reportedVel={7:F1} elapsedMs={8} behindMs={9}",
                slave.ObjId, zoneKey, x, y, bridgeX, bridgeY,
                BoatSeamHandoffRules.AlongTrackMetres(
                    x, y, bridgeX, bridgeY, planted.VelX, planted.VelY),
                reportedSpeed, elapsedMs, behindMs);

            // Do not follow while B is short of the plant. The 400 ms backstop was the
            // rollback: first B update behind the client, then a backward correction.
            return;
        }

        FinishBoatSeamHandoff(slave);
    }

    /// <summary>
    /// Overlap path: client stays on A. Closed-loop the shortfall on B's first consumed
    /// pose. Finish when B is at cruise, or A is silent after any impulse lands.
    /// </summary>
    private static bool TryFinishOverlapSeam(
        Slave slave, uint zoneKey, float reportedSpeed, float x, float y, long now)
    {
        if (!BoatZoneSimRules.ShouldOverlapOldSim(slave.ZoneAnnouncedTo, zoneKey))
            return false;

        if (!BoatZoneSimRules.IsInsideShipWorld(x, y))
        {
            Logger.Info(
                "Seam overlap warmup ignored obj={0} zone={1} pos=({2:F1},{3:F1}) (origin)",
                slave.ObjId, zoneKey, x, y);
            return true;
        }

        var snapshotSpeed = slave.SeamHandoff is { } live
            ? BoatSeamHandoffRules.LinearSpeed(live)
            : 0f;
        var liveThrottle = BoatSeamPredictRules.LiveThrottle(
            0, slave.ThrottleRequest, slave.Throttle);
        var expectedCruise = BoatZoneSimRules.ExpectedCruiseForWarmup(
            slave.SeamTargetSpeed, snapshotSpeed, liveThrottle);

        var replantAge = slave.SeamReplantAtMs == 0 ? -1 : now - slave.SeamReplantAtMs;
        var alongTrack = AlongTrackVsStreamedBody(slave, x, y, now);
        var silent = IsOldSimSilent(slave, now);
        if (BoatZoneSimRules.ShouldImpulseWarmup(x, y, reportedSpeed, expectedCruise, replantAge) &&
            (slave.SeamImpulseAtMs == 0 ||
             now - slave.SeamImpulseAtMs >= BoatZoneSimRules.ImpulseSettleMs))
        {
            slave.SeamTargetSpeed = expectedCruise;
            slave.SeamCorrectionZone = zoneKey;
            // One impulse carries both: the speed the flush lost and the distance B fell behind
            // A while it was slow. A separate catch-up 200 ms later started too late to land
            // before the fail-safe (live 18:23–18:25: gap 2.0–2.6 m, switch with 1.6–1.9 m left).
            var catchUp = silent || slave.SeamCatchUpSpeed > 0f ? 0f : BoatZoneSimRules.CatchUpSpeed(alongTrack);
            ApplySeamSpeedCorrection(slave, zoneKey, reportedSpeed, catchUp);
            if (catchUp > 0f)
                NoteSeamCatchUp(slave, zoneKey, catchUp, alongTrack, now);
            slave.SeamImpulseAtMs = now;
            Logger.Info(
                "Seam overlap closed-loop obj={0} zone={1} arrived={2:F1} expectedCruise={3:F1} along={4:F2} m",
                slave.ObjId, zoneKey, reportedSpeed, expectedCruise, alongTrack);
        }

        if (slave.SeamReplantAtMs == 0)
            return true;

        replantAge = now - slave.SeamReplantAtMs;
        var msSinceImpulse = slave.SeamImpulseAtMs == 0 ? -1 : now - slave.SeamImpulseAtMs;
        var msSinceCatchUp = slave.SeamCatchUpAtMs == 0 ? -1 : now - slave.SeamCatchUpAtMs;

        // B has its speed back but is still behind the body the client is watching and no
        // catch-up has been sent yet: close the gap on B before follow switches.
        if (!silent &&
            slave.SeamCatchUpSpeed <= 0f &&
            BoatZoneSimRules.IsBehindStreamedBody(alongTrack) &&
            BoatZoneSimRules.ShouldAcceptWarmupHandoff(x, y, reportedSpeed, expectedCruise, replantAge, msSinceImpulse) &&
            (slave.SeamImpulseAtMs == 0 || msSinceImpulse >= BoatZoneSimRules.ImpulseSettleMs))
        {
            var catchUp = BoatZoneSimRules.CatchUpSpeed(alongTrack);
            if (catchUp > 0f)
            {
                ApplySeamCatchUp(slave, zoneKey, catchUp);
                NoteSeamCatchUp(slave, zoneKey, catchUp, alongTrack, now);
                slave.SeamImpulseAtMs = now;
                msSinceImpulse = 0;
                msSinceCatchUp = 0;
            }
        }

        if (!BoatZoneSimRules.ShouldFinishOverlapSeam(
                silent, replantAge, x, y, reportedSpeed, expectedCruise, msSinceImpulse, alongTrack, msSinceCatchUp))
        {
            Logger.Info(
                "Seam overlap waiting to switch obj={0} zone={1} pos=({2:F1},{3:F1}) " +
                "settleMs={4} arrived={5:F1} expectedCruise={6:F1} along={7:F2} m",
                slave.ObjId, zoneKey, x, y, replantAge, reportedSpeed, expectedCruise, alongTrack);
            return true;
        }

        Logger.Info(
            "Seam overlap follow switch obj={0} zone={1} pos=({2:F1},{3:F1}) settleMs={4} silent={5} along={6:F2} m",
            slave.ObjId, zoneKey, x, y, replantAge, silent, alongTrack);
        FinishBoatSeamHandoff(slave, reportedSpeed, expectedCruise);
        return true;
    }

    /// <summary>
    /// Signed metres the incoming body is past the body the client is being streamed (the old
    /// simulator's last report advanced to now along its own velocity). Negative = behind.
    /// Zero when there is no streamed body to compare against.
    /// </summary>
    private static float AlongTrackVsStreamedBody(Slave slave, float x, float y, long now)
    {
        if (slave.SimulatedShipState is not { } streamed || slave.SimulatedShipStateAtMs == 0)
            return 0f;

        var dt = Math.Clamp(now - slave.SimulatedShipStateAtMs, 0, BoatSeamPredictRules.MaxPredictAgeMs) / 1000f;
        var refX = streamed.X + BoatSeamPredictRules.DecodeVelMetresPerSecond(streamed.VelX) * dt;
        var refY = streamed.Y + BoatSeamPredictRules.DecodeVelMetresPerSecond(streamed.VelY) * dt;
        return BoatSeamHandoffRules.AlongTrackMetres(x, y, refX, refY, streamed.VelX, streamed.VelY);
    }

    /// <summary>
    /// Forward (or, negative, backward) impulse on the incoming body along its own bow. Same
    /// channel as the speed correction. A positive value closes the along-track gap to the
    /// streamed body; the same magnitude negated at the follow switch takes the excess back.
    /// </summary>
    private static void ApplySeamCatchUp(Slave slave, uint zoneKey, float speed)
    {
        float[] vel = new float[3];
        float[] angVel = new float[3];
        float[] impulse = new float[3];
        float[] angImpulse = new float[3];
        BoatSeamImpulse.BuildVectors(speed, vel, angVel, impulse, angImpulse);

        var self = new SkillCasterUnit(slave.ObjId);
        WorldIntegration.RelaySeamImpulseToZone?.Invoke(
            slave.ObjId, zoneKey, self, vel, angVel, impulse, angImpulse);
    }

    /// <summary>
    /// Captures the outgoing simulator's last streamed body so the relay can blend the incoming
    /// body onto its track (<see cref="BoatSeamBlendRules"/>). Only across a live overlap: a first
    /// summon or a silent A has no track to continue.
    /// </summary>
    private static void ArmSeamBlend(Slave slave, uint liveZone, uint zoneKey)
    {
        slave.SeamBlendStartMs = 0;
        slave.SeamBlendOffset = null;
        slave.SeamBlendFrom = null;
        if (!BoatZoneSimRules.ShouldOverlapOldSim(liveZone, zoneKey) ||
            slave.SimulatedShipState is not { } from || slave.SimulatedShipStateAtMs == 0)
        {
            return;
        }

        slave.SeamBlendFrom = from;
        slave.SeamBlendFromAtMs = slave.SimulatedShipStateAtMs;
        slave.SeamBlendStartMs = Environment.TickCount64;
    }

    private static void NoteSeamCatchUp(Slave slave, uint zoneKey, float catchUp, float alongTrack, long now)
    {
        slave.SeamCatchUpSpeed = catchUp;
        slave.SeamCatchUpAtMs = now;
        Logger.Info(
            "Seam catch-up obj={0} zone={1} behind={2:F2} m added={3:F1} m/s",
            slave.ObjId, zoneKey, -alongTrack, catchUp);
    }

    /// <summary>
    /// The catch-up was a velocity pulse: once follow switches the extra way has done its job
    /// and would otherwise ride on as an over-cruise the thrust law only bleeds slowly.
    /// </summary>
    private static void TakeBackSeamCatchUp(Slave slave, uint zoneKey, float incomingReportedSpeed, float expectedCruise)
    {
        if (slave.SeamCatchUpSpeed <= 0f)
            return;
        var removed = BoatZoneSimRules.CatchUpTakeBack(slave.SeamCatchUpSpeed, incomingReportedSpeed, expectedCruise);
        if (removed > 0f)
            ApplySeamCatchUp(slave, zoneKey, -removed);
        Logger.Info(
            "Seam catch-up taken back obj={0} zone={1} added={2:F1} removed={3:F1} m/s reported={4:F1} cruise={5:F1}",
            slave.ObjId, zoneKey, slave.SeamCatchUpSpeed, removed, incomingReportedSpeed, expectedCruise);
        slave.SeamCatchUpSpeed = 0f;
        slave.SeamCatchUpAtMs = 0;
    }

    /// <summary>
    /// Carry the crossing's way on the seed, then restore only the shortfall. A rest seed
    /// plus a full cruise impulse stacked on leftover way (live 18.8 → 22.1).
    /// </summary>
    private static void ApplySeamOpenLoopRestore(Slave slave, uint zoneKey)
    {
        var target = slave.SeamTargetSpeed;
        var throttle = BoatSeamPredictRules.LiveThrottle(
            slave.SimulatedShipState?.Throttle ?? 0, slave.ThrottleRequest, slave.Throttle);
        var seeded = slave.SimulatedShipState?.ReportedSpeed ?? 0f;
        if (seeded <= 0f)
            seeded = slave.SimulatedSpeed;
        var measured = target > 0f ? target : seeded;

        if (!BoatSeamImpulse.TryBuildOpenLoopRestore(
                BoatSeamImpulse.Enabled, measured, 0, throttle, seeded, out var speed))
        {
            Logger.Info(
                "Seam overlap open-loop skipped obj={0} zone={1} target={2:F1} seeded={3:F1} throttle={4}",
                slave.ObjId, zoneKey, measured, seeded, throttle);
            return;
        }

        slave.SeamTargetSpeed = 0f;
        slave.SeamCorrectionZone = 0;

        float[] vel = new float[3];
        float[] angVel = new float[3];
        float[] impulse = new float[3];
        float[] angImpulse = new float[3];
        BoatSeamImpulse.BuildVectors(speed, vel, angVel, impulse, angImpulse);

        var self = new SkillCasterUnit(slave.ObjId);
        WorldIntegration.RelaySeamImpulseToZone?.Invoke(
            slave.ObjId, zoneKey, self, vel, angVel, impulse, angImpulse);

        Logger.Info(
            "Seam overlap open-loop restore obj={0} zone={1} added={2:F1} m/s seeded={3:F1} throttle={4}",
            slave.ObjId, zoneKey, speed, seeded, throttle);
    }

    /// <summary>
    /// Points World at the newly armed simulator, hands riders over, and drops the previous unit
    /// without sending a sim-off.
    /// </summary>
    public static void FinishBoatSeamHandoff(Slave slave, float incomingReportedSpeed = 0f, float expectedCruise = 0f)
    {
        if (slave == null)
            return;

        var zoneKey = slave.ZoneSimPendingFor;
        if (zoneKey == 0)
            return;

        var liveZone = slave.ZoneAnnouncedTo;
        slave.ZoneAnnouncedTo = zoneKey;
        slave.ZoneSimPendingFor = 0;
        TakeBackSeamCatchUp(slave, zoneKey, incomingReportedSpeed, expectedCruise);
        ArmSeamBlend(slave, liveZone, zoneKey);

        if (BoatZoneSimRules.ShouldOverlapOldSim(liveZone, zoneKey))
        {
            if (!HandoffPassengersToZone(slave, liveZone, zoneKey))
            {
                DropHullFromZone(slave, liveZone);
                AbandonBoatWithoutZoneHost(slave, zoneKey);
                return;
            }

            DropHullFromZone(slave, liveZone, detachPassengers: false);
            RefreshClientSeatBinds(slave);
        }

        Logger.Info(
            "Ship simulation enabled for slave obj={0} zoneId={1} droppedOld={2}",
            slave.ObjId, zoneKey,
            BoatZoneSimRules.ShouldOverlapOldSim(liveZone, zoneKey) ? liveZone : 0);
        slave.SeamHandoff = null;
        slave.SeamImpulseAtMs = 0;
        slave.SeamBridgeBehindAtMs = 0;
        slave.SeamReplantAtMs = 0;
        slave.SeamTargetSpeed = 0f;
        slave.SeamCorrectionZone = 0;
        slave.SeamHelmQueue.Clear();
        slave.SeamSpeedProbes = SeamSpeedProbeCount;
    }

    /// <summary>
    /// Arms a measured correction for a hull that has just been handed to a new simulator: records the
    /// speed it arrived with so the first pose the new zone publishes can be compared against it.
    /// </summary>
    /// <remarks>
    /// Open-loop restores were tried both ways and neither works, because the impulse channel is
    /// additive and the fraction of a seeded velocity that survives the handover is not fixed. Sending
    /// the whole speed on top of the seed overshot (10.6 m/s handed over, 15.9 arrived); sending nothing
    /// left the hull short (13.5 in, 8.1 out — and 11.3 in, 6.7 out). The gap is worth measuring rather
    /// than predicting, which is what <see cref="ApplySeamSpeedCorrection"/> does.
    /// </remarks>
    private static void ArmSeamSpeedCorrection(Slave slave, uint zoneKey)
    {
        slave.SeamTargetSpeed = 0f;
        slave.SeamCorrectionZone = 0;

        if (!BoatSeamImpulse.Enabled)
            return;

        // A hull whose helm is at rest crossed under no power; leave it alone.
        if (slave.Throttle == 0)
            return;

        // Prefer the speed the outgoing simulator reported for itself over the one inferred from the
        // positions it published: the inferred figure is an average over a sampling window and reads low.
        var reported = slave.SimulatedShipState?.ReportedSpeed ?? 0f;
        var target = reported > 0f ? reported : slave.SimulatedSpeed;

        var age = Environment.TickCount64 - slave.SimulatedSpeedAtMs;
        if (target < BoatSeamImpulse.MinCruiseSpeed || age >= BoatSeamImpulse.FreshnessWindowMs)
            return;

        slave.SeamTargetSpeed = target;
        slave.SeamCorrectionZone = zoneKey;
    }

    /// <summary>
    /// Closes the gap between the speed a hull crossed a seam with and the speed its new simulator
    /// actually gave it, using the one channel that can set a controlled hull's velocity directly.
    /// </summary>
    /// <remarks>
    /// Fires once per crossing, on the first usable pose — after the flush transient has been
    /// discarded. The impulse is a velocity change applied on top of whatever the body already
    /// carries: the seed handed the crossing's way to that body at the flush, so the correction is
    /// the surviving shortfall — a fraction the flush transient and drag make unpredictable, which
    /// is why it is measured. A self-cast rotates the vectors by the
    /// hull's live rotation, which is why the magnitude goes on local +Y and arrives on the bow.
    /// The receiver drops an impulse for a hull it does not consider controlled, so this cannot
    /// run before the arming message.
    /// </remarks>
    /// <param name="reportedSpeed">Speed in the pose the new simulator just published.</param>
    /// <param name="catchUp">
    /// Extra forward speed folded into the same impulse so the body also closes the distance it
    /// fell behind the streamed one (<see cref="BoatZoneSimRules.CatchUpSpeed"/>).
    /// </param>
    public static void ApplySeamSpeedCorrection(Slave slave, uint zoneKey, float reportedSpeed, float catchUp = 0f)
    {
        if (slave == null || slave.SeamTargetSpeed <= 0f || slave.SeamCorrectionZone != zoneKey)
            return;

        var target = slave.SeamTargetSpeed;

        // One shot either way: a hull that arrived up to speed needs nothing, and re-arming on later
        // poses would fight the thrust curve for the rest of the voyage.
        slave.SeamTargetSpeed = 0f;
        slave.SeamCorrectionZone = 0;

        var thrustCutoff = ShipPoseSeed.EffectiveMaxVelocity(slave);
        if (!BoatSeamImpulse.TryBuildSeamCorrection(
                BoatSeamImpulse.Enabled, target, reportedSpeed, thrustCutoff, out var deficit))
        {
            Logger.Info(
                "Seam speed correction not needed obj={0} zone={1} target={2:F1} m/s arrived={3:F1} m/s " +
                "thrustCutoff={4:F1} m/s",
                slave.ObjId, zoneKey, target, reportedSpeed, thrustCutoff);
            deficit = 0f;
            if (catchUp <= 0f)
                return;
        }

        float[] vel = new float[3];
        float[] angVel = new float[3];
        float[] impulse = new float[3];
        float[] angImpulse = new float[3];
        BoatSeamImpulse.BuildVectors(deficit + catchUp, vel, angVel, impulse, angImpulse);

        var self = new SkillCasterUnit(slave.ObjId);
        WorldIntegration.RelaySeamImpulseToZone?.Invoke(
            slave.ObjId, zoneKey, self, vel, angVel, impulse, angImpulse);

        Logger.Info(
            "Seam speed correction obj={0} zone={1} target={2:F1} m/s arrived={3:F1} m/s added={4:F1} m/s " +
            "catchUp={5:F1} m/s thrustCutoff={6:F1} m/s",
            slave.ObjId, zoneKey, target, reportedSpeed, deficit, catchUp, thrustCutoff);
    }

    /// <summary>
    /// Moves riders from the live simulator onto the newly armed one. They stay on the old hull
    /// until this runs so the old dedicate never sees a passenger whose root it already deleted.
    /// </summary>
    private static bool HandoffPassengersToZone(Slave slave, uint fromZone, uint toZone)
    {
        foreach (var (attachPoint, passenger) in slave.AttachedCharacters.ToList())
        {
            if (passenger == null)
                continue;

            var passengerBody = WorldIntegration.BuildWzUnitStateBody(passenger);
            if (passengerBody is { Length: > 0 })
            {
                var accepted = WorldIntegration.RelayCharacterZoneHandoff?.Invoke(
                    passenger.ObjId, fromZone, toZone, passengerBody) ?? true;
                if (!accepted)
                    return false;
            }

            ReplayUnitBuffsToZone(
                passenger, toZone, (int)(slave.Transform?.InstanceId ?? 0), "rider");
            WorldIntegration.RelayUnitAttachToZoneId?.Invoke(
                toZone, passenger.ObjId, slave.ObjId, (byte)attachPoint, true);
        }

        return true;
    }

    /// <summary>
    /// Re-sends the client bind after the rider's zone unit is Created standing. Occupy and the
    /// wheel mesh are client-side; attach with <see cref="AttachUnitReason.None"/> does not
    /// restart them.
    /// </summary>
    private static void RefreshClientSeatBinds(Slave slave)
    {
        foreach (var (attachPoint, passenger) in slave.AttachedCharacters.ToList())
        {
            if (passenger == null)
                continue;

            passenger.BroadcastPacket(
                new SCUnitAttachedPacket(passenger.ObjId, attachPoint, AttachUnitReason.NewMaster, slave.ObjId),
                true);
            if (attachPoint != AttachPointKind.Driver)
                continue;

            if (BoatHelmSeatRules.ShouldRebindHelmAtFollowSwitch)
            {
                passenger.BroadcastPacket(
                    new SCSlaveBoundPacket(passenger.Id, slave.MasterWorldId, slave.ObjId), true);
                SlaveOccupyBuffs.ApplyBuffEffects(passenger, 0, slave);
            }
            Logger.Info(
                "Boat seam seat refresh slave obj={0} rider={1} point={2} rebind={3}",
                slave.ObjId, passenger.ObjId, attachPoint, BoatHelmSeatRules.ShouldRebindHelmAtFollowSwitch);
        }
    }

    /// <summary>
    /// Removes this hull from one dedicate. Does not send <c>control=0</c>: the unit is going away,
    /// and that packet is what freezes a still-present hull at a seam.
    /// </summary>
    /// <param name="detachPassengers">
    /// False once the riders have already been handed to another zone. A character handoff removes the
    /// rider from the zone it left, so detaching it there afterwards names a unit that zone no longer
    /// has and it answers <c>OnUnitDetached: cannot find child</c> — once per seam crossing.
    /// </param>
    public static void DropHullFromZone(Slave slave, uint zoneId, bool detachPassengers = true)
    {
        if (slave == null || zoneId == 0)
            return;

        if (detachPassengers)
        {
            foreach (var (attachPoint, passenger) in slave.AttachedCharacters.ToList())
            {
                if (passenger != null)
                {
                    WorldIntegration.RelayUnitAttachToZoneId?.Invoke(
                        zoneId, passenger.ObjId, slave.ObjId, (byte)attachPoint, false);
                }
            }
        }

        var childSlaves = new List<Slave>();
        var doodads = new List<Doodad>();
        CollectBoatAttachments(slave, childSlaves, doodads);
        var childIds = BoatDespawnRules.UnitIdsToRemoveFromZone(
            slave.ObjId, childSlaves.Select(c => c.ObjId));
        var doodadIds = BoatDespawnRules.DoodadIdsToRemoveFromZone(doodads.Select(d => d.ObjId));

        // Children first: a parent remove that leaves them behind is what re-parented leftover
        // masts onto the next hull that reused this id.
        foreach (var unitId in childIds)
            WorldIntegration.RelayUnitRemovedToZoneId?.Invoke(zoneId, unitId);

        foreach (var doodadId in doodadIds)
            WorldIntegration.RelayRemoveDoodadToZoneId?.Invoke(zoneId, doodadId);

        Logger.Info(
            "WZUnitRemoved for slave obj={0} zoneId={1} children={2} doodads={3}",
            slave.ObjId, zoneId, childIds.Count - (slave.ObjId == 0 ? 0 : 1), doodadIds.Count);
    }

    /// <summary>
    /// Drops every dedicate that still has this hull or an attachment (live + a pending create +
    /// any child whose own zone key differs). Used on despawn.
    /// </summary>
    public static void WithdrawBoatFromZone(Slave slave)
    {
        if (slave == null)
            return;

        var announced = slave.ZoneAnnouncedTo;
        var pending = slave.ZoneSimPendingFor;
        var extraZones = new List<uint>();
        var childSlaves = new List<Slave>();
        var doodads = new List<Doodad>();
        CollectBoatAttachments(slave, childSlaves, doodads);
        foreach (var child in childSlaves)
        {
            var zoneId = child.Transform?.ZoneId ?? 0;
            if (zoneId != 0)
                extraZones.Add(zoneId);
        }

        foreach (var doodad in doodads)
        {
            var zoneId = doodad.Transform?.ZoneId ?? 0;
            if (zoneId != 0)
                extraZones.Add(zoneId);
        }

        var dropped = new HashSet<uint>();
        if (BoatZoneSimRules.ShouldDropStalePending(pending, announced, 0))
        {
            DropHullFromZone(slave, pending);
            dropped.Add(pending);
        }

        if (announced != 0)
        {
            DropHullFromZone(slave, announced);
            dropped.Add(announced);
        }

        foreach (var zoneId in BoatDespawnRules.ZonesThatMayHoldAttachments(announced, pending, extraZones))
        {
            if (dropped.Contains(zoneId))
                continue;
            DropHullFromZone(slave, zoneId);
        }

        BoatZoneKeyStability.Clear(slave.ObjId);
        slave.ZoneAnnouncedTo = 0;
        slave.ZoneSimEnabledFor = 0;
        slave.ZoneSimPendingFor = 0;
        slave.SeamTargetSpeed = 0f;
        slave.SeamCorrectionZone = 0;
        slave.SeamArmedAtMs = 0;
        slave.SeamCatchUpSpeed = 0f;
        slave.SeamCatchUpAtMs = 0;
        slave.SeamBlendStartMs = 0;
        slave.SeamBlendOffset = null;
        slave.SeamBlendFrom = null;
        slave.StreamedShipZoneId = 0;
        slave.StreamedShipTime = 0;
        slave.StreamedShipSteering = 0;
        slave.StreamedShipTimeOffset = 0;
        slave.StreamedShipAtMs = 0;
        slave.WaterlineSimHeldOff = false;
        slave.WaterlineRecoverAtMs = 0;
    }

    /// <summary>
    /// Nested-first walk of equipment slaves and attached doodads (sails, figureheads, ladders).
    /// </summary>
    internal static void CollectBoatAttachments(Slave hull, List<Slave> slaves, List<Doodad> doodads)
    {
        if (hull == null)
            return;

        if (hull.AttachedDoodads != null)
        {
            foreach (var doodad in hull.AttachedDoodads)
            {
                if (doodad is { ObjId: > 0 })
                    doodads.Add(doodad);
            }
        }

        if (hull.AttachedSlaves == null)
            return;

        foreach (var child in hull.AttachedSlaves)
        {
            if (child == null)
                continue;
            CollectBoatAttachments(child, slaves, doodads);
            if (child.ObjId > 0)
                slaves.Add(child);
        }
    }

    /// <summary>
    /// Hides attachments and releases their broadcast ids after the zones have been told to drop them.
    /// </summary>
    private static void TearDownBoatAttachments(Slave hull)
    {
        var slaves = new List<Slave>();
        var doodads = new List<Doodad>();
        CollectBoatAttachments(hull, slaves, doodads);

        foreach (var doodad in doodads)
        {
            doodad.IsPersistent = false;
            doodad.ParentWorld?.SpawnManager.CancelDespawn(doodad);
            doodad.Delete();
            if (doodad.ObjId == 0)
                continue;
            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            doodad.ObjId = 0;
        }

        foreach (var child in slaves)
        {
            child.ParentWorld?.SpawnManager.CancelDespawn(child);
            child.Delete();
            if (child.ObjId != 0)
            {
                ObjectIdManager.Instance.ReleaseId(child.ObjId);
                child.ObjId = 0;
            }

            if (child.TlId != 0)
            {
                TlIdManager.Instance.ReleaseId(child.TlId);
                child.TlId = 0;
            }

            child.AttachedDoodads?.Clear();
            child.AttachedSlaves?.Clear();
        }

        hull.AttachedDoodads?.Clear();
        hull.AttachedSlaves?.Clear();
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
            // The escape can land the hull in another zone key; commit immediately so the escape
            // packet reaches the dedicate that will simulate the ship.
            var template = mySlave.ParentWorld?.Template;
            var p = mySlave.Transform.World.Position;
            if (template != null)
            {
                var sampled = WorldManager.Instance.GetZoneId(template, p.X, p.Y);
                var zoneKey = BoatZoneKeyStability.ForceCommit(mySlave.ObjId, sampled);
                if (zoneKey > 0 && mySlave.Transform.ZoneId != zoneKey)
                    mySlave.Transform.ZoneId = zoneKey;
                if (mySlave.ZoneAnnouncedTo != zoneKey && zoneKey > 0)
                    CommitBoatZoneHandoff(mySlave, mySlave.ZoneAnnouncedTo, zoneKey);
            }

            p = mySlave.Transform.World.Position;
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
