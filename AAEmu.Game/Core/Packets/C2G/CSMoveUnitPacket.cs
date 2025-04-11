using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSMoveUnitPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private uint _objId;
    private MoveType _moveType;

    public CSMoveUnitPacket() : base(CSOffsets.CSMoveUnitPacket, 1)
    {
    }

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

        // if movement is forbidden when teleporting to instances, then to exit
        if (character.DisabledSetPosition) return;

        var targetUnit = WorldManager.Instance.GetBaseUnit(_objId);

        // Invalid Object ?
        if (targetUnit == null)
        {
            // TODO по какой то причине объект удалили из региона, наверное нужно его как то вернуть назад 
            // TODO for some reason the object has been removed from the region, you probably need to get it back somehow
            Logger.Warn("Invalid target {0} from {1}", _objId, character.Name);
            return;
        }

        // We are not controlling our main character
        switch (_moveType)
        {
            case ShipRequestMoveType srmt:
                {
                    // TODO: Validate if we are in the driver seat
                    // We are controlling a ship
                    // Logger.Debug("ShipRequestMoveType - Throttle: {0} - Steering {1}", srmt.Throttle, srmt.Steering);
                    if (targetUnit is not Slave ship)
                        return;

                    // TODO: Validate if targetUnit is actually a ship

                    ship.ThrottleRequest = srmt.Throttle;
                    ship.SteeringRequest = srmt.Steering;

                    // Make sure driver is attached to the ship
                    character.Transform.Parent = ship.Transform;
                    // Actual movement and sending of packets is handle by the Physics Engine
                    break;
                }
            case VehicleMoveType vmt:
                {
                    // TODO: Validate if we are in the driver seat
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

                    // TODO: Validate if targetUnit is a "car"

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

                    // It moving Pets, handle Pet XP for moving
                    if (targetUnit is Mate mate)
                    {
                        // Pet moved
                        RemoveEffects(targetUnit, _moveType);

                        // TODO: Check if we're the owner, or allowed to otherwise control this pet
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
                        // TODO : check target has Telekinesis buff if target is a player
                        // Just forward it to the packet, not safe for exploits/hacking
                        // We moved
                        RemoveEffects(player, _moveType);

                        if (player.IsRiding)
                        {
                            // Если мы сидим на питомце и Parent = null, насильно спешиваем персонажа для предотвращения сбоя клиента
                            // If we are sitting on a pet and Parent = null, we force it on there to prevent client crashing
                            if (player.Transform.Parent == null)
                            {
                                var mate2 = MateManager.Instance.GetActiveMate(character.ObjId);
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

                    var isStandingOnObject = ((MoveTypeFlags)dmt.Flags).HasFlag(MoveTypeFlags.StandingOnObject);
                    // Don't know why, but we need to Ignore GcId 1, it probably has some special meaning like "current parent"
                    var parentObject = isStandingOnObject && dmt.GcId > 1
                        ? WorldManager.Instance.GetBaseUnit(dmt.GcId)
                        : null;
                    var isSticky = ((MoveTypeActorFlags)dmt.ActorFlags).HasFlag(MoveTypeActorFlags.HangingFromObject);

                    if ((targetUnit.Transform.Parent != null) && (parentObject == null))
                    {
                        // No longer standing on object?
                        var oldParentObj = targetUnit.Transform.Parent.GameObject?.ObjId ?? 0;
                        targetUnit.Transform.Parent = null;

                        character.SendDebugMessage(
                            $"|cFF884444{targetUnit.Name} ({targetUnit.ObjId}) no longer standing on Object {oldParentObj} " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }
                    else if ((targetUnit.Transform.Parent == null) && (parentObject != null))
                    {
                        // Standing on a new object ?
                        targetUnit.Transform.Parent = parentObject.Transform;

                        character.SendDebugMessage(
                            $"|cFF448844{targetUnit.Name} ({targetUnit.ObjId}) standing on Object {parentObject.Name} ({parentObject.ObjId}) " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }
                    else if ((targetUnit.Transform.Parent != null) &&
                             (targetUnit.Transform.Parent.GameObject != null) &&
                             (parentObject != null) &&
                             (targetUnit.Transform.Parent.GameObject.ObjId != parentObject.ObjId))
                    {
                        // Changed to standing on different object ?
                        targetUnit.Transform.Parent = parentObject.Transform;

                        character.SendDebugMessage(
                            $"|cFF448888{targetUnit.Name} ({targetUnit.ObjId}) moved to standing on new Object {parentObject.Name} ({parentObject.ObjId}) " +
                            $"@ x{dmt.X:F1} y{dmt.Y:F1} z{dmt.Z:F1} || World: {targetUnit.Transform.World}|r");
                    }

                    // If ActorFlag 0x40 is no longer set, it means we're no longer climbing/holding onto something
                    if ((targetUnit.Transform.StickyParent != null) && !isSticky)
                        targetUnit.Transform.StickyParent = null;

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

                    if ((targetUnit is Character other) && (other.ObjId != character.ObjId))
                    {
                        // TODO : check target has Telekinesis buff if target is a player
                        // Just forward it to the packet, not safe for exploits/hacking
                    }

                    // Actually update the position
                    targetUnit.Transform.Local.SetPosition(dmt.X, dmt.Y, dmt.Z,
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationX),
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationY),
                        (float)MathUtil.ConvertDirectionToRadian(dmt.RotationZ));
                    //Logger.Info($"SetPosition:World {targetUnit.ObjId} is moving X={targetUnit.Transform.World.Position.X} Y={targetUnit.Transform.World.Position.Y}");
                    //Logger.Info($"SetPosition:Local {targetUnit.ObjId} is moving X={dmt.X} Y={dmt.Y}");
                    targetUnit.BroadcastPacket(new SCOneUnitMovementPacket(_objId, dmt), true);
                    targetUnit.Transform.FinalizeTransform(true);

                    // Handle Fall Velocity
                    if ((dmt.FallVel > 0) && (targetUnit is Unit unit))
                    {
                        var fallDmg = unit.DoFallDamage(dmt.FallVel);
                        // character.SendMessage("{0} took {1} fall damage {2}/{3} HP left", unit.Name, fallDmg, unit.Hp, unit.MaxHp);
                    }

                    break;
                }
            default:
                Logger.Warn("Unknown MoveType: {0} by {1} for {2} ", _moveType, character.Name, targetUnit.Name);
                break;
        }
    }

    private static void RemoveEffects(BaseUnit unit, MoveType moveType)
    {
        if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
            unit.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
    }

    public override string Verbose()
    {
        return " - " + (_moveType?.Type.ToString() ?? "none") + " " + (WorldManager.Instance.GetGameObject(_objId)?.DebugName() ?? "(" + _objId.ToString() + ")");
    }
}
