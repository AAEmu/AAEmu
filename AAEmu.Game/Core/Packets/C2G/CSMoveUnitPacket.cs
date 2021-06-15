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
                        _log.Debug("ShipRequestMoveType - Throttle: " + srmt.Throttle + " - Steering: " +
                                   srmt.Steering);
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
                        _log.Debug("VehicleMoveType AngleVelocity XYZ: " + vmt.AngVelX.ToString("F1") + " , " +
                                   vmt.AngVelY.ToString("F1") + " , " + vmt.AngVelZ.ToString("F1") + "\n" +
                                   "Steering: " + vmt.Steering + " - WheelAngleVelocity: (" +
                                   string.Join(" , ", vmt.WheelAngVel.ToArray()) + " )");

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
                        targetUnit.Transform.FinalizeTransform();
                        
                        // Handle Fall Velocity
                        if ((dmt.FallVel > 0) && (targetUnit is Unit unit))
                        {
                            var fallDmg = unit.DoFallDamage(dmt.FallVel);
                            character.SendMessage("{0} took {1} fall damage {2}/{3} HP left", unit.Name, fallDmg, unit.Hp, unit.MaxHp);
                        }

                        break;
                    }
                default:
                    _log.Warn("Unknown MoveType: {0} by {1} for {2} ", _moveType, character.Name, targetUnit.Name);
                    break;
            }

        }

        public void ExecuteOld()
        {
            // _moveType.Flags
            // 0x02 : Moving
            // 0x04 : Stopping (released movement keys)
            // 0x06 : Jumping
            // 0x40 : Standing on something
            _log.Debug("CSMoveUnitPacket(" + _moveType.Type + ") \nScType: " + _moveType.ScType + " - Flags: " + _moveType.Flags.ToString("X") + " - " +
                       "Phase: " + _moveType.Phase + " - Time: " + _moveType.Time + " - " +
                       "Sender: " + Connection.ActiveChar.Name + " (" + Connection.ActiveChar.ObjId + ") - " +
                       "Obj: " + (WorldManager.Instance.GetBaseUnit(_objId)?.Name ?? "<null>") + " (" + _objId + ") \n" +
                       "XYZ: " + _moveType.X.ToString("F1") + " , " + _moveType.Y.ToString("F1") + " , " + _moveType.Z.ToString("F1") + " - " +
                       "Rot: " + _moveType.RotationX.ToString() + " , " + _moveType.RotationY.ToString() + " , " + _moveType.RotationZ.ToString() + " - " +
                       "VelXYZ: " + _moveType.VelX.ToString("F1") + " , " + _moveType.VelY.ToString("F1") + " , " + _moveType.VelZ.ToString("F1")
                       );
            // TODO: We can probably rewrite this
            if (_objId != Connection.ActiveChar.ObjId) // Can be mate
            {
                // We are not controlling our main character
                switch (_moveType)
                {
                    case ShipRequestMoveType srmt:
                        {
                            _log.Debug("ShipRequestMoveType - Throttle: " + srmt.Throttle + " - Steering: " + srmt.Steering);
                            var bu = WorldManager.Instance.GetBaseUnit(_objId);
                            if (!(bu is Slave slave))
                                return;
                            //var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
                            //if (slave == null) return;

                            slave.ThrottleRequest = srmt.Throttle;
                            slave.SteeringRequest = srmt.Steering;

                            // Also update driver's position
                            Connection.ActiveChar.Transform.Parent = slave.Transform;
                            // Connection.ActiveChar.SetPosition(slave.Transform.World.Position.X, slave.Transform.World.Position.Y, slave.Transform.World.Position.Z, 0, 0, 0);
                            break;
                        }
                    case VehicleMoveType vmt:
                        {
                            // Steering: Value between -1.0 and +1.0
                            // WheelAngVel: Velocity on individual wheels? (note: cart/wagon has "no wheels")
                            _log.Debug("VehicleMoveType AngleVelocity XYZ: " + vmt.AngVelX.ToString("F1") + " , " +vmt.AngVelY.ToString("F1") + " , " + vmt.AngVelZ.ToString("F1") + "\n"+
                                       "Steering: " + vmt.Steering+" - WheelAngleVelocity: (" + string.Join(" , ",vmt.WheelAngVel.ToArray())+" )");
                                var (rotDegX, rotDegY, rotDegZ) = MathUtil.GetSlaveRotationInDegrees(vmt.RotationX, vmt.RotationY, vmt.RotationZ);
                            //var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationFromDegrees(rotDegX, rotDegY, rotDegZ);

                            var bu = WorldManager.Instance.GetBaseUnit(_objId);
                            if (!(bu is Slave slave))
                                return;
                            
                            Connection.ActiveChar.Transform.Parent = slave.Transform;
                            slave.Transform.Local.SetPosition(vmt.X, vmt.Y, vmt.Z, rotDegX, rotDegY, rotDegZ);
                            // slave.SetPosition(vmt.X, vmt.Y, vmt.Z, MathUtil.ConvertRadianToDirection(rotDegX), MathUtil.ConvertRadianToDirection(rotDegY), MathUtil.ConvertRadianToDirection(rotDegZ));
                            slave.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vmt), true);
                            slave.Transform.FinalizeTransform();
                            /*
                            Connection.ActiveChar.Transform.Parent = null;
                            Connection.ActiveChar.Transform.Local.SetPosition(vmt.X, vmt.Y, vmt.Z);
                            Connection.ActiveChar.Transform.FinalizeTransform();
                            */
                            break;
                        }
                    // TODO : check target has Telekinesis buff
                    case UnitMoveType dmt:
                        {
                            var unit = WorldManager.Instance.GetUnit(_objId);
                            if (unit == null)
                                break;
                            unit.Transform.Local.SetPosition(dmt.X, dmt.Y, dmt.Z);
                            unit.BroadcastPacket(new SCOneUnitMovementPacket(_objId, dmt), true);
                            break;
                        }
                }

                var mateInfo = MateManager.Instance.GetActiveMateByMateObjId(_objId);
                if (mateInfo == null) return;

                if (_moveType is UnitMoveType mType)
                {
                    if ((mType.Flags & 0x40) != 0)
                    {
                        var parentObject = WorldManager.Instance.GetBaseUnit(mType.GcId);
                        if ((parentObject != null) && (parentObject.Transform != null))
                        {
                            if (mateInfo.Transform.Parent != parentObject.Transform)
                                Connection.ActiveChar.SendMessage(
                                    "|cFF888822Mate Standing on Object: {0} ({4}) @ x{1} y{2} z{3} || World: {5}|r", 
                                    mType.GcId, 
                                    mType.X.ToString("F1"), mType.Y.ToString("F1"), mType.Z.ToString("F1"), 
                                    parentObject.Name, mateInfo.Transform.World.ToString());
                            mateInfo.Transform.Parent = parentObject.Transform;

                            mateInfo.SetPosition(mType.X, mType.Y, mType.Z,
                                (float)MathUtil.ConvertDirectionToRadian(mType.RotationX),
                                (float)MathUtil.ConvertDirectionToRadian(mType.RotationY),
                                (float)MathUtil.ConvertDirectionToRadian(mType.RotationZ));
                        }
                        else
                        {
                            Connection.ActiveChar.SendMessage(
                                "|cFFFF0000Mate Standing on Unknown Object: {0}|r @ x{1} y{2} z{3}", mType.GcId,
                                mType.X.ToString("F1"), mType.Y.ToString("F1"), mType.Z.ToString("F1"));
                            // Somehow didn't send correct parent object maybe ?
                            // Just use the proposed position instead
                            // TODO: Make this exploit-proof
                            mateInfo.Transform.Parent = null;
                            mateInfo.SetPosition(mType.X2, mType.Y2, mType.Z2, mType.RotationX, mType.RotationY,
                                mType.RotationZ);
                        }
                    }
                    else
                    {
                        if (mateInfo.Transform.Parent != null)
                        {
                            mateInfo.Transform.Parent = null;
                            Connection.ActiveChar.SendMessage(
                                "|cFF888844Mate No longer standing on Object: x{0} y{1} z{2} || World: {3}|r", 
                                mType.X.ToString("F1"), mType.Y.ToString("F1"), mType.Z.ToString("F1"), 
                                mateInfo.Transform.World.ToString());
                        }
                    }
                }

                RemoveEffects(mateInfo, _moveType);
                mateInfo.SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);
                mateInfo.Transform.FinalizeTransform();
               
            }
            else
            {
                // We are controlling our own character directly
                RemoveEffects(Connection.ActiveChar, _moveType);

                if (!(_moveType is UnitMoveType mType))
                    return;
                /*
                _log.Debug("ActorFlags: 0x{0} - ClimbData: {1} - GcId: {2}", 
                    mType.ActorFlags.ToString("X"),
                    mType.ClimbData.ToString("X"), 
                    mType.GcId.ToString(("X")));
                */
                // 0x04 : Standing on solid
                // 0x10 : Jumping
                // 0x20 : Standing on top of object
                // 0x40 : Hanging onto object ?
                
                
                if ((mType.ActorFlags & 0x20) != 0)
                {
                    // ActorFlag 0x20 means we're standing on another object ?
                    
                    var parentObject = WorldManager.Instance.GetBaseUnit(mType.GcId);
                    if ((parentObject != null) && (parentObject.Transform != null))
                    {
                        /*
                        if (Connection.ActiveChar.Transform.Parent != parentObject.Transform)
                            Connection.ActiveChar.SendMessage(
                                "|cFF888822Standing on Object: {0} ({4}) @ x{1} y{2} z{3} || World: {5}|r", mType.GcId, mType.X,
                                mType.Y, mType.Z, parentObject.Name, Connection.ActiveChar.Transform.World.ToString());
                        */
                        Connection.ActiveChar.Transform.Parent = parentObject.Transform;

                        /*
                        Connection.ActiveChar.Transform.Local.SetPosition(mType.X,mType.Y,mType.Z);
                        Connection.ActiveChar.Transform.Local.SetRotation(
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationX),
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationY),
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationZ));
                        */
                        
                        Connection.ActiveChar.SetPosition(mType.X, mType.Y, mType.Z,
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationX),
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationY),
                            (float)MathUtil.ConvertDirectionToRadian(mType.RotationZ));
                    }
                    else
                    {
                        Connection.ActiveChar.SendMessage("|cFFFF0000Standing on Unknown Object: {0}|r @ x{1} y{2} z{3}",mType.GcId,mType.X,mType.Y,mType.Z);
                        // Somehow didn't send correct parent object maybe ?
                        // Just use the proposed position instead
                        // TODO: Make this exploit-proof
                        Connection.ActiveChar.Transform.Parent = null;
                        Connection
                            .ActiveChar
                            .SetPosition(mType.X2, mType.Y2, mType.Z2, mType.RotationX, mType.RotationY, mType.RotationZ);
                    }
                    
                }
                else
                {
                    /*
                    if (Connection.ActiveChar.Transform.Parent != null)
                        Connection.ActiveChar.SendMessage(
                            "|cFF888844No longer standing on Object: x{0} y{1} z{2} || World: {3}|r", mType.X,
                            mType.Y, mType.Z, Connection.ActiveChar.Transform.World.ToString());
                    */

                    // We're "standing" on the main world/nothing, if not hanging on something, reset the parent
                    if ((mType.ActorFlags & 0x40) != 0)
                    {
                        var stickyVerticalOffset = (float)(mType.ClimbData & 0x1FFF);// / 8192f * 100f; // 13 bits
                        var stickyHorizontalOffset = (float)((mType.ClimbData & 0x00FFE000) >> 13);// / 256f * 100f; // 11 bits
                        var stickyRotationOffset = (float)((sbyte)((mType.ClimbData & 0xFF000000) >> 24)) / 254f * 360f; // 8 bits
                        _log.Debug("StickyPos - Vertical: {0}/8192 , Horizontal: {1}/2048, Rotation: {2}°", 
                            stickyVerticalOffset, stickyHorizontalOffset, stickyRotationOffset.ToString("F1"));
                    }
                    else
                    {
                        // If ActorFlag 0x40 is no longer set, it means we're no longer climbing/holding onto something
                        Connection.ActiveChar.Transform.StickyParent = null;
                    }

                    Connection.ActiveChar.Transform.Parent = null;
                    Connection
                        .ActiveChar
                        .SetPosition(mType.X, mType.Y, mType.Z, mType.RotationX, mType.RotationY, mType.RotationZ);
                }
                
                // Handle Fall Velocity
                if (mType.FallVel > 0)
                    Connection.ActiveChar.DoFallDamage(mType.FallVel);
                
                Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(_objId, mType), true);
                //Connection.ActiveChar.SendMessage("Pos: {0}",Connection.ActiveChar.Transform.ToString());
            }
        }

        private static void RemoveEffects(BaseUnit unit, MoveType moveType)
        {
            if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
                unit.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
        }
    }
}
