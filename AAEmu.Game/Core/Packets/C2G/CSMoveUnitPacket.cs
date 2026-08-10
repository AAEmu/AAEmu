using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSMoveUnitPacket() : GamePacket(CSOffsets.CSMoveUnitPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private uint _objId;
    private MoveType _moveType;

    public override void Read(PacketStream stream)
    {
        _objId = stream.ReadBc();

        var type = (MoveTypeEnum)stream.ReadByte();
        _moveType = MoveType.GetType(type);
        stream.Read(_moveType);
    }

    public override void Execute()
    {
        // _moveType.Flags
        // 0x02 : Moving
        // 0x04 : Stopping (released movement keys)
        // 0x06 : Jumping
        // 0x40 : Standing on something
        /*
        Logger.Debug("CSMoveUnitPacket(" + _moveType.Type + ") \nScType: " + _moveType.ScType + " - Flags: " +
                   _moveType.Flags.ToString("X") + " - " +
                   "Phase: " + _moveType.Phase + " - Time: " + _moveType.Time + " - " +
                   "Sender: " + Connection.ActiveChar.Name + " (" + Connection.ActiveChar.ObjId + ") - " +
                   "Obj: " + (WorldManager.Instance.GetBaseUnit(_objId)?.Name ?? "<null>") + " (" + _objId +
                   ") \n" +
                   "XYZ: " + _moveType.X.ToString("F1") + " , " + _moveType.Y.ToString("F1") + " , " +
                   _moveType.Z.ToString("F1") + " - " +
                   "Rot: " + _moveType.RotationX.ToString() + " , " + _moveType.RotationY.ToString() + " , " +
                   _moveType.RotationZ.ToString() + " - " +
                   "VelXYZ: " + _moveType.VelX.ToString("F1") + " , " + _moveType.VelY.ToString("F1") + " , " +
                   _moveType.VelZ.ToString("F1")
        );
        */

        var character = Connection.ActiveChar;

        if (character == null) return;
        character.LastPacketActivityTime = DateTime.UtcNow;

        // if movement is forbidden when teleporting to instances, then to exit
        if (character.DisabledSetPosition) return;

        // Commercial: zone owns locomotion — forward CS move as WZ; do not broadcast Game SC as authority.
        if (WorldIntegration.ZoneAuthority && WorldIntegration.RelayMoveToZone != null)
        {
            var mirrorTarget = character.ParentWorld.GetBaseUnit(_objId);
            if (mirrorTarget == null || !CanControlMovement(character, mirrorTarget, _moveType))
            {
                Logger.Warn(
                    "Rejected Zone movement type {0} for target {1} from {2} ({3})",
                    _moveType.Type, _objId, character.Name, character.ObjId);
                return;
            }

            var moveBody = new PacketStream();
            moveBody.Write((byte)_moveType.Type);
            moveBody.Write(_moveType);
            WorldIntegration.RelayMoveToZone(_objId, moveBody.GetBytes());

            // Keep local transform for region/interest so CS/SC glue does not desync, but no SC broadcast.
            if (_moveType is UnitMoveType umt && _objId == character.ObjId)
            {
                // Anchor the physics clock to the client's own reported tPhy. MirrorMovementStreamTask uses
                // this to stamp synthesized NPC keepalive movements with a tPhy in the client's clock domain
                // (the native zone streams no idle movement, so without this the client's world clock stalls
                // and it cleanly quits a few seconds after entering an idle area).
                character.PhysTimeAnchor = _moveType.Time;
                character.PhysTimeAnchorTick = Environment.TickCount64;

                // Sit/bond (buff 4645 remove_on_move, chair DoodadFuncAttachment): ZoneAuthority used to
                // return before RemoveEffects / unbond, so move never cleared the sit state → stuck.
                RemoveEffects(character, _moveType);
                if (character.Bonding != null &&
                    (_moveType.VelX != 0 || _moveType.VelY != 0 || _moveType.VelZ != 0))
                {
                    var bonding = character.Bonding;
                    var bondedDoodad = bonding.GetOwner();
                    var doodadObjId = bonding.ObjId;
                    bondedDoodad?.Seat.UnLoadPassenger(character, doodadObjId);
                    character.Bonding.SetOwner(null);
                    character.Bonding = null;
                    character.Transform.Parent = null;
                    character.Transform.StickyParent = null;
                    character.BroadcastPacket(
                        new SCUnbondDoodadPacket(character.ObjId, character.Id, doodadObjId), true);
                    if (bonding.IsMovingParent)
                        WorldIntegration.RelayBondDoodadToZone?.Invoke(character.ObjId, bonding, false);
                    // Sit buff 4645 has remove_on_unbond + remove_on_move; RemoveEffects above
                    // covers move. Explicitly drop sit buff if still present after unbond.
                    if (character.Buffs.CheckBuff(4645))
                        character.Buffs.RemoveBuff(4645);
                }

                // Mast / ladder hang (StickyParent) and BindSlave seats: ZoneAuthority used to
                // rewrite Local world coords while still parented, or clear Parent without
                // UnbindSlave — player stayed "on the mast" with no get-off. Jump and hang-bit
                // drop must Unbind + SCUnhung (client hang state), not only null StickyParent.
                var jumping = umt.Flags.HasFlag(MoveTypeFlags.Jumping)
                    || ((MoveTypeActorFlags)umt.ActorFlags).HasFlag(MoveTypeActorFlags.Jumping);
                var hanging = ((MoveTypeActorFlags)umt.ActorFlags).HasFlag(MoveTypeActorFlags.HangingFromObject);
                if (jumping || (!hanging && umt.GcId == 0))
                    TryDismountSlaveOrHang(character, jumping);

                // Bound to helm/sail: never keep mast hang — client blocks T as airborne.
                if (character.AttachedPoint != AttachPointKind.None &&
                    (character.Transform.StickyParent != null || hanging))
                    ClearHang(character, reason: 0);
                else if (character.Transform.StickyParent != null && !hanging)
                    ClearHang(character, reason: 0); // climbed off

                ApplyGroundContact(character, umt);

                character.Transform.Local.SetPosition(
                    umt.X, umt.Y, umt.Z,
                    (float)MathUtil.ConvertDirectionToRadian(umt.RotationX),
                    (float)MathUtil.ConvertDirectionToRadian(umt.RotationY),
                    (float)MathUtil.ConvertDirectionToRadian(umt.RotationZ));
                character.Transform.FinalizeTransform();
                character.SetPlayerMoved();

                // Fan this player's movement out to everyone around them.
                //
                // The zone-authority design assumed the zone would stream player movement back to us as
                // part of ZWUnitMovements, which is why this path deliberately sent no SC packet. It does
                // not: measured with two clients walking towards each other, every ZWUnitMovements batch
                // contained only NPC ids and never a player's - so nobody ever saw anybody else move.
                //
                // Sent to everyone BUT the mover: the client integrates its own character locally and
                // reports the finished state through CSMoveUnit, so echoing it back fights that prediction.
                character.BroadcastPacket(new SCOneUnitMovementPacket(_objId, umt), false);
            }
            else if (_moveType is UnitMoveType controlledUnitMove)
            {
                RemoveEffects(mirrorTarget, _moveType);
                mirrorTarget.Transform.Local.SetPosition(
                    controlledUnitMove.X, controlledUnitMove.Y, controlledUnitMove.Z,
                    (float)MathUtil.ConvertDirectionToRadian(controlledUnitMove.RotationX),
                    (float)MathUtil.ConvertDirectionToRadian(controlledUnitMove.RotationY),
                    (float)MathUtil.ConvertDirectionToRadian(controlledUnitMove.RotationZ));
                mirrorTarget.Transform.FinalizeTransform();

                // Same gap as for the character above: the zone never streams this back, so without a
                // broadcast a ridden mount would stand still for everybody else. Mirrors what the
                // pre-zone path does for mates.
                mirrorTarget.BroadcastPacket(new SCOneUnitMovementPacket(_objId, controlledUnitMove), false);
            }
            else if (_moveType is VehicleMoveType vehicleMove && mirrorTarget is Slave vehicle)
            {
                var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationInDegrees(
                    vehicleMove.RotationX, vehicleMove.RotationY, vehicleMove.RotationZ);
                character.Transform.Parent = vehicle.Transform;
                vehicle.Transform.Local.SetPosition(
                    vehicleMove.X, vehicleMove.Y, vehicleMove.Z, rotX, rotY, rotZ);
                vehicle.Transform.FinalizeTransform();

                // As above. The driver's own client authored this position, so the relay filters the
                // zone's copy of wheeled vehicles back out for them anyway; observers need it from here.
                vehicle.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vehicleMove), false);
            }
            else if (_moveType is ShipRequestMoveType shipRequest && mirrorTarget is Slave ship)
            {
                // The zone owns the hull, so the helm request above is what actually moves it. These two
                // are kept for the packets that report the current helm position back to observers.
                ship.ThrottleRequest = shipRequest.Throttle;
                ship.SteeringRequest = shipRequest.Steering;
                character.Transform.Parent = ship.Transform;
            }

            return;
        }

        var targetUnit = character.ParentWorld.GetBaseUnit(_objId);

        if (targetUnit == null)
        {
            Logger.Warn("Rejected movement for missing target {0} from {1} ({2})", _objId, character.Name, character.ObjId);
            return;
        }

        if (!CanControlMovement(character, targetUnit, _moveType))
        {
            Logger.Warn(
                "Rejected movement type {0} for target {1} from {2} ({3})",
                _moveType.Type, _objId, character.Name, character.ObjId);
            return;
        }

        // We are not controlling our main character
        switch (_moveType)
        {
            case ShipRequestMoveType srmt:
                {
                    // We are controlling a ship
                    // Logger.Debug("ShipRequestMoveType - Throttle: {0} - Steering {1}", srmt.Throttle, srmt.Steering);
                    if (targetUnit is not Slave ship)
                        return;

                    ship.ThrottleRequest = srmt.Throttle;
                    ship.SteeringRequest = srmt.Steering;

                    // Make sure driver is attached to the ship
                    character.Transform.Parent = ship.Transform;
                    // Actual movement and sending of packets is handle by the Physics Engine
                    break;
                }
            case VehicleMoveType vmt:
                {
                    // Steering: Value between -1.0 and +1.0
                    // WheelAngVel: Velocity on individual wheels? (note: cart/wagon has "no wheels")
                    /*
                    Logger.Debug("VehicleMoveType AngleVelocity XYZ: " + vmt.AngVelX.ToString("F1") + " , " +
                               vmt.AngVelY.ToString("F1") + " , " + vmt.AngVelZ.ToString("F1") + "\n" +
                               "Steering: " + vmt.Steering + " - WheelAngleVelocity: (" +
                               string.Join(" , ", vmt.WheelAngVel.ToArray()) + " )");
                    */

                    if (targetUnit is not Slave car)
                        return;

                    var (rotDegX, rotDegY, rotDegZ) = MathUtil.GetSlaveRotationInDegrees(vmt.RotationX, vmt.RotationY, vmt.RotationZ);

                    // Make sure driver is attached to car
                    character.Transform.Parent = car.Transform;
                    car.Transform.Local.SetPosition(vmt.X, vmt.Y, vmt.Z, rotDegX, rotDegY, rotDegZ);
                    car.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vmt), true);
                    car.Transform.FinalizeTransform(); // Propagate position updates to all children
                    break;
                }
            case UnitMoveType dmt:
                {
                    // Logger.Debug($"{targetUnit.Name} => ActorFlags: 0x{dmt.ActorFlags:X} - ClimbData: {dmt.ClimbData:X} - GcId: {dmt.GcId}");

                    // Its moving Pets, handle Pet XP for moving
                    if (targetUnit is Mate mate)
                    {
                        // Pet moved
                        RemoveEffects(targetUnit, _moveType);

                        if (dmt.VelX != 0 || dmt.VelY != 0)
                            mate.StartUpdateXp(character);
                        else
                            mate.StopUpdateXp();

                        foreach (var (_, passengerInfo) in mate.Passengers)
                        {
                            var passenger = WorldManager.Instance.GetCharacterByObjId(passengerInfo._objId);
                            if (passenger != null)
                            {
                                // passenger.Transform = mate.Transform.CloneDetached(passenger);
                                RemoveEffects(passenger, _moveType);
                            }
                        }
                    }

                    // If controlling character, but it's riding something, sync parent with the mount
                    if (targetUnit is Character player)
                    {
                        // We moved
                        RemoveEffects(player, _moveType);

                        if (player.IsRiding)
                        {
                            // Если мы сидим на питомце и Parent = null, насильно спешиваем персонажа для предотвращения сбоя клиента
                            // If we are sitting on a pet and Parent = null, we force it on there to prevent client crashing
                            if (player.Transform.Parent == null)
                            {
                                var mate2 = Connection.ActiveChar.ParentWorld.MateManager.GetActiveMates(character.Id).FirstOrDefault();
                                if (mate2 != null)
                                {
                                    player.Transform.Parent = mate2.Transform;
                                }
                            }
                            // We're riding a pet, we don't care about the rest of this function
                            // If we're riding the pet, we should only care about the pet's movement
                            Logger.Debug($"{targetUnit.Name} IsRiding, ignoring movement request");
                            return;
                        }

                        // Player moved
                        player.SetPlayerMoved();
                    }

                    var isStandingOnObject = dmt.Flags.HasFlag(MoveTypeFlags.StandingOnObject);
                    // Don't know why, but we need to Ignore GcId 1, it probably has some special meaning like "current parent"
                    var parentObject = isStandingOnObject && dmt.GcId > 1
                        ? character.ParentWorld.GetBaseUnit(dmt.GcId)
                        : null;
                    var isSticky = ((MoveTypeActorFlags)dmt.ActorFlags).HasFlag(MoveTypeActorFlags.HangingFromObject);

                    if (targetUnit.Transform.Parent != null && parentObject == null)
                    {
                        // No longer standing on object?
                        var oldParentObj = targetUnit.Transform.Parent.GameObject?.ObjId ?? 0;
                        targetUnit.Transform.Parent = null;

                        character.SendDebugMessage(
                            $"|cFF884444{targetUnit.Name} ({targetUnit.ObjId}) no longer standing on Object {oldParentObj} " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }
                    else if (targetUnit.Transform.Parent == null && parentObject != null)
                    {
                        // Standing on a new object ?
                        targetUnit.Transform.Parent = parentObject.Transform;

                        character.SendDebugMessage(
                            $"|cFF448844{targetUnit.Name} ({targetUnit.ObjId}) standing on Object {parentObject.Name} ({parentObject.ObjId}) " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }
                    else if (targetUnit.Transform.Parent is { GameObject: not null } &&
                             parentObject != null &&
                             targetUnit.Transform.Parent.GameObject.ObjId != parentObject.ObjId)
                    {
                        // Changed to standing on different object ?
                        targetUnit.Transform.Parent = parentObject.Transform;

                        character.SendDebugMessage(
                            $"|cFF448888{targetUnit.Name} ({targetUnit.ObjId}) moved to standing on new Object {parentObject.Name} ({parentObject.ObjId}) " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }

                    // If ActorFlag 0x40 is no longer set, it means we're no longer climbing/holding onto something
                    if (targetUnit.Transform.StickyParent != null && !isSticky)
                        ClearHang(targetUnit, reason: 0);

                    // Debug Climb Data
                    /*
                    if (dmt.ClimbData != 0)
                    {
                        var stickyVerticalOffset =
                            (float)(dmt.ClimbData & 0x1FFF); // / 8192f * 100f; // 13 bits
                        var stickyHorizontalOffset =
                            (float)((dmt.ClimbData & 0x00FFE000) >> 13); // / 256f * 100f; // 11 bits
                        var stickyRotationOffset =
                            (float)((sbyte)((dmt.ClimbData & 0xFF000000) >> 24)) / 254f * 360f; // 8 bits
                        Logger.Debug(
                            "ClimbData - {0} ({1}) - Vertical: {2}/8192 , Horizontal: {3}/2048, Rotation: {4}°",
                            targetUnit.Name, targetUnit.ObjId,
                            stickyVerticalOffset, stickyHorizontalOffset, stickyRotationOffset.ToString("F1"));
                    }
                    */

                    // Actually update the position
                    targetUnit.Transform.Local.SetPosition(dmt.X, dmt.Y, dmt.Z,
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationX),
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationY),
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationZ));
                    //Logger.Info($"SetPosition:World {targetUnit.ObjId} is moving X={targetUnit.Transform.World.Position.X} Y={targetUnit.Transform.World.Position.Y}");
                    //Logger.Info($"SetPosition:Local {targetUnit.ObjId} is moving X={dmt.X} Y={dmt.Y}");
                    targetUnit.BroadcastPacket(new SCOneUnitMovementPacket(_objId, dmt), true);
                    targetUnit.Transform.FinalizeTransform();

                    // Handle Fall Velocity
                    if (dmt.FallVel > 0 && targetUnit is Unit unit)
                    {
                        // A unit being carried reports velocity relative to the mover, not the
                        // ground: riding a tower lift or an airship produces values well past the
                        // 32000 instant-death threshold in DoFallDamage. Anything parented,
                        // stuck to a surface, or bonded to a seat is not falling under its own
                        // weight, so the client's figure is meaningless for damage.
                        var carried = targetUnit.Transform.Parent != null ||
                                      targetUnit.Transform.StickyParent != null ||
                                      (targetUnit as Character)?.Bonding != null;
                        if (carried)
                        {
                            Logger.Debug(
                                "Ignoring FallVel {0} for carried unit {1} ({2})",
                                dmt.FallVel, targetUnit.Name, targetUnit.ObjId);
                        }
                        else
                        {
                            _ = unit.DoFallDamage(dmt.FallVel);
                        }
                    }

                    break;
                }
            default:
                Logger.Warn($"Unknown MoveType: {_moveType} by {character.Name} for {targetUnit.Name}");
                break;
        }
    }

    private static void RemoveEffects(BaseUnit unit, MoveType moveType)
    {
        if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
            unit.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
    }

    /// <summary>
    /// Roots the actor to whatever it is standing on, so the position that follows is applied in the
    /// right space.
    /// </summary>
    /// <remarks>
    /// While an actor stands on a moving entity (gimmick lift, ship deck) the client reports its
    /// position in that entity's local space and names the carrier in <c>actor.gcId</c>
    /// (ActorFlags 0x20/0x40 - see <see cref="UnitMoveType.Read"/>). Applying those coordinates
    /// without re-parenting drops the character onto the platform offset in world space - a few
    /// metres from the origin - which puts its interest region at the origin (surrounding objects
    /// despawn) and its Z below <c>OceanLevel</c> (drowning).
    /// </remarks>
    private static void ApplyGroundContact(Character character, UnitMoveType umt)
    {
        var carrier = umt.GcId != 0 ? character.ParentWorld?.GetGameObject(umt.GcId) : null;

        if (carrier != null && carrier.ObjId != character.ObjId)
        {
            if (!ReferenceEquals(character.Transform.Parent, carrier.Transform))
                character.Transform.Parent = carrier.Transform;
            return;
        }

        // No carrier reported. Bonding owns the parent link while seated, so leave that alone.
        // Slave BindSlave seats must Unbind (SCUnitDetached + Zone), not only null Parent —
        // otherwise the player stays attached (mast stuck) with AttachedPoint still set.
        if (character.Bonding == null && character.Transform.Parent != null)
        {
            if (character.AttachedPoint != AttachPointKind.None)
                TryDismountSlaveOrHang(character, jumping: false);
            else
                character.Transform.Parent = null;
        }
    }

    /// <summary>
    /// Leave a slave seat / mast or ladder hang. Jump always forces off; free movement does too
    /// when not actively hanging (ActorFlags hanging bit).
    /// </summary>
    private static void TryDismountSlaveOrHang(Character character, bool jumping)
    {
        var slaveManager = character.ParentWorld?.SlaveManager;
        if (slaveManager != null)
        {
            var onSlave = slaveManager.GetIsMounted(character.ObjId, out var attachPoint);
            if (onSlave != null)
            {
                // Driver helm: only leave on explicit jump / get-off skill — not every move packet.
                if (attachPoint == AttachPointKind.Driver && !jumping)
                    return;

                slaveManager.UnbindSlave(character, onSlave.TlId, AttachUnitReason.None);
                Logger.Debug(
                    "Dismount slave {0} attach={1} jump={2} char={3}",
                    onSlave.ObjId, attachPoint, jumping, character.Name);
                return;
            }
        }

        // Mast/ladder hang is StickyParent + CS/SCUnhang — not BindSlave Mast0 (equip mesh points).
        if (character.Transform.StickyParent != null && jumping)
            ClearHang(character, reason: 7); // jumped off
    }

    /// <summary>
    /// Mirror <see cref="CSUnhangPacket"/> so the client leaves climb/hang state (reason 0 climb-off, 7 jump-off).
    /// Always includes self — BroadcastPacket(false) never delivers SCUnhung to the caster, so the client
    /// stays in skill_source_is_hanging ("Can't be used while airborne") and T/get-off never fires.
    /// </summary>
    private static void ClearHang(BaseUnit unit, uint reason)
    {
        var sticky = unit.Transform.StickyParent;
        var targetObjId = sticky?.GameObject?.ObjId ?? 0;
        if (sticky != null)
            unit.Transform.StickyParent = null;
        else if (unit is not Character)
            return;

        // Even with StickyParent already null (CSUnhang cleared server state), re-notify the client
        // when this is a Character — BindSlave often races after Unhang with hang still latched client-side.
        if (unit is Character ch)
            ch.BroadcastPacket(new SCUnhungPacket(unit.ObjId, targetObjId, reason), true);
        else
            unit.BroadcastPacket(new SCUnhungPacket(unit.ObjId, targetObjId, reason), false);

        if (sticky?.GameObject is Slave stickySlave)
            ShipHarpoonRopeController.BreakRopeForClients(stickySlave, cutouted: false);
    }

    private static bool CanControlMovement(Character character, BaseUnit target, MoveType moveType)
    {
        if (target is Character)
            return target.ObjId == character.ObjId && moveType is UnitMoveType;

        if (target is Mate mate)
        {
            // Ordered attack owns mate transform (UseMateAutoAttackSkillTask.MoveTowards). Client
            // follow/recall CSMoveUnit was overwriting chase so Skill.Use stayed TooFarRange.
            if (mate.IsAutoAttack)
                return false;

            return moveType is UnitMoveType
                   && (mate.OwnerObjId == character.ObjId
                   || mate.Passengers.TryGetValue(AttachPointKind.Driver, out var passenger)
                   && passenger._objId == character.ObjId);
        }

        if (target is Slave slave)
        {
            var isDriver = slave.AttachedCharacters.TryGetValue(AttachPointKind.Driver, out var driver)
                           && driver?.ObjId == character.ObjId;
            if (!isDriver)
                return false;

            return moveType switch
            {
                ShipRequestMoveType => slave.Template?.IsABoat() == true,
                VehicleMoveType => slave.Template?.IsClientDrivenLandVehicle() == true,
                _ => false
            };
        }

        return false;
    }

    public override string Verbose()
    {
        return " - " + (_moveType?.Type.ToString() ?? "none") + " " + (Connection.ActiveChar.ParentWorld.GetGameObject(_objId)?.DebugName() ?? "(" + _objId + ")");
    }
}
