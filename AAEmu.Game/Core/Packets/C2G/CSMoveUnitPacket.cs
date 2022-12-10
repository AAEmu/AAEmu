using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Mails;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMoveUnitPacket : GamePacket
    {
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
            _log.Debug("CSMoveUnitPacket(" + _moveType.Type + ") \nScType: " + _moveType.ScType + " - Flags: " +
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
            var targetUnit = WorldManager.Instance.GetBaseUnit(_objId);

            // Invalid Object ?
            if (targetUnit == null)
            {
                _log.Warn("Invalid target {0} from {1}", _objId, character.Name);
                return;
            }

            // We are not controlling our main character
            switch (_moveType)
            {
                case ShipRequestMoveType srmt:
                    {
                        // TODO: Validate if we are in the driver seat
                        // We are controlling a ship
                        // _log.Debug("ShipRequestMoveType - Throttle: {0} - Steering {1}", srmt.Throttle, srmt.Steering);
                        if (!(targetUnit is Slave ship))
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
                        _log.Debug("VehicleMoveType AngleVelocity XYZ: " + vmt.AngVelX.ToString("F1") + " , " +
                                   vmt.AngVelY.ToString("F1") + " , " + vmt.AngVelZ.ToString("F1") + "\n" +
                                   "Steering: " + vmt.Steering + " - WheelAngleVelocity: (" +
                                   string.Join(" , ", vmt.WheelAngVel.ToArray()) + " )");
                        */

                        if (!(targetUnit is Slave car))
                            return;

                        // TODO: Validate if targetUnit is a "car"

                        var (rotDegX, rotDegY, rotDegZ) =
                            MathUtil.GetSlaveRotationInDegrees(vmt.RotationX, vmt.RotationY, vmt.RotationZ);

                        // Make sure driver is attached to car
                        character.Transform.Parent = car.Transform;
                        car.Transform.Local.SetPosition(vmt.X, vmt.Y, vmt.Z, rotDegX, rotDegY, rotDegZ);
                        car.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vmt), true);
                        car.Transform.FinalizeTransform(); // Propagate position updates to all children
                        break;
                    }
                case UnitMoveType dmt:
                    {
                        /*
                        _log.Debug("ActorFlags: 0x{0} - ClimbData: {1} - GcId: {2}", 
                            mType.ActorFlags.ToString("X"),
                            mType.ClimbData.ToString("X"), 
                            mType.GcId.ToString(("X")));
                        */

                        // If the StandingOn flag is set, then fill in the parentObject to use
                        var parentObject = ((MoveTypeFlags)dmt.Flags).HasFlag(MoveTypeFlags.StandingOnObject)
                            ? WorldManager.Instance.GetBaseUnit(dmt.GcId)
                            : null;
                        var isSticky =
                            ((MoveTypeActorFlags)dmt.ActorFlags).HasFlag(MoveTypeActorFlags.HangingFromObject);

                        // Don't know why, but we need to Ignore Id 1, it probably has some special meaning like "current parent"
                        if (dmt.GcId == 1)
                        {
                            // _log.Warn($"Flags: {dmt.Flags}, GcId: {dmt.GcId}, ClimbData: {dmt.ClimbData}, PartId: {dmt.GcPartId} ?");
                            parentObject = null;
                        }

                        // We moved
                        RemoveEffects(targetUnit, _moveType);

                        if ((targetUnit.Transform.Parent != null) && (parentObject == null))
                        {
                            // No longer standing on object ?
                            var oldParentObj = targetUnit.Transform.Parent.GameObject.ObjId;
                            targetUnit.Transform.Parent = null;
                            
                            character.SendMessage(
                                "|cFF884444{0} ({1}) no longer standing on Object {2} @ x{3} y{4} z{5} || World: {6}|r",
                                targetUnit.Name, targetUnit.ObjId,
                                oldParentObj,
                                dmt.X.ToString("F1"), dmt.Y.ToString("F1"), dmt.Z.ToString("F1"), 
                                targetUnit.Transform.World.ToString());
                            
                        }
                        else
                        if ((targetUnit.Transform.Parent == null) && (parentObject != null))
                        {
                            // Standing on a object ?
                            targetUnit.Transform.Parent = parentObject.Transform;
                            
                            character.SendMessage(
                                "|cFF448844{0} ({1}) standing on Object {2} ({3}) @ x{4} y{5} z{6} || World: {7}|r",
                                targetUnit.Name, targetUnit.ObjId,
                                parentObject.Name, parentObject.ObjId,
                                dmt.X.ToString("F1"), dmt.Y.ToString("F1"), dmt.Z.ToString("F1"), 
                                targetUnit.Transform.World.ToString());
                            
                        }
                        else 
                        if ((targetUnit.Transform.Parent != null) && (parentObject != null) && (targetUnit.Transform.Parent.GameObject.ObjId != parentObject.ObjId))
                        {
                            // Changed to standing on different object ? 
                            targetUnit.Transform.Parent = parentObject.Transform;
                            
                            character.SendMessage(
                                "|cFF448888{0} ({1}) moved to standing on new Object {2} ({3}) @ x{4} y{5} z{6} || World: {7}|r",
                                targetUnit.Name, targetUnit.ObjId,
                                parentObject.Name, parentObject.ObjId,
                                dmt.X.ToString("F1"), dmt.Y.ToString("F1"), dmt.Z.ToString("F1"), 
                                targetUnit.Transform.World.ToString());
                            
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
                            _log.Debug(
                                "ClimbData - {0} ({1}) - Vertical: {2}/8192 , Horizontal: {3}/2048, Rotation: {4}°",
                                targetUnit.Name, targetUnit.ObjId,
                                stickyVerticalOffset, stickyHorizontalOffset, stickyRotationOffset.ToString("F1"));
                        }
                        */

                        if ((targetUnit is Character player) && (player.ObjId != character.ObjId))
                        {
                            // TODO : check target has Telekinesis buff if target is a player
                            // Just forward it to the packet, not safe for exploits/hacking
                        }
                        else if (targetUnit is Mate pet)
                        {
                            // TODO: Check if we're the owner, or allowed to otherwise control this pet
                        }

                        // Actually update the position
                        targetUnit.Transform.Local.SetPosition(dmt.X, dmt.Y, dmt.Z,
                            (float)MathUtil.ConvertDirectionToRadian(dmt.RotationX),
                            (float)MathUtil.ConvertDirectionToRadian(dmt.RotationY),
                            (float)MathUtil.ConvertDirectionToRadian(dmt.RotationZ));
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
                    _log.Warn("Unknown MoveType: {0} by {1} for {2} ", _moveType, character.Name, targetUnit.Name);
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
            return " - " + (_moveType?.Type.ToString() ?? "none") + " " + (WorldManager.Instance.GetGameObject(_objId)?.DebugName() ?? "("+_objId.ToString()+")");
        }

    }
}
