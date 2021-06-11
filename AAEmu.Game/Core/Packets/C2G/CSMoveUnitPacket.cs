using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
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
            _log.Debug("CSMoveUnitPacket(" + _moveType.Type + ") \nScType: " + _moveType.ScType + " - Flags: " + _moveType.Flags.ToString("X") + " - " +
                       "Phase: " + _moveType.Phase + " - Time: " + _moveType.Time + " - " +
                       "Sender: " + Connection.ActiveChar.Name + " (" + Connection.ActiveChar.ObjId + ") - " +
                       "Obj: " + (WorldManager.Instance.GetBaseUnit(_objId)?.Name ?? "<null>") + " (" + _objId + ") \n" +
                       "XYZ: " + _moveType.X.ToString("F1") + " , " + _moveType.Y.ToString("F1") + " , " + _moveType.Z.ToString("F1") + " - " +
                       "Rot: " + _moveType.RotationX.ToString() + " , " + _moveType.RotationY.ToString() + " , " + _moveType.RotationZ.ToString() + " - " +
                       "VelXYZ: " + _moveType.VelX.ToString("F1") + " , " + _moveType.VelY.ToString("F1") + " , " + _moveType.VelZ.ToString("F1"));
            
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

                RemoveEffects(mateInfo, _moveType);
                /*
                mateInfo.Transform.Local.SetPosition(_moveType.X,_moveType.Y,_moveType.Z);
                mateInfo.Transform.Local.SetRotationDegree(
                    (float)MathUtil.ConvertSbyteDirectionToDegree(_moveType.RotationX),
                    (float)MathUtil.ConvertSbyteDirectionToDegree(_moveType.RotationY),
                    (float)MathUtil.ConvertSbyteDirectionToDegree(_moveType.RotationZ));
                */
                mateInfo.SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);

                var movements = new List<(uint, MoveType)> {(_objId, _moveType)};

                // Att1 & Att2 should be handled by mate.SetPosition, no need for them to be here
                if (mateInfo.Att1 > 0)
                {
                    var owner = WorldManager.Instance.GetCharacterByObjId(mateInfo.Att1);
                    if (owner != null)
                    {
                        RemoveEffects(owner, _moveType);
                        owner.SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);
                        movements.Add((owner.ObjId, _moveType));
                    }
                }

                if (mateInfo.Att2 > 0)
                {
                    var passenger = WorldManager.Instance.GetCharacterByObjId(mateInfo.Att2);
                    if (passenger != null)
                    {
                        RemoveEffects(passenger, _moveType);
                        passenger.SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);
                        movements.Add((passenger.ObjId, _moveType));
                    }
                }
                
                mateInfo.BroadcastPacket(new SCUnitMovementsPacket(movements.ToArray()), false);
            }
            else
            {
                // We are controlling our own character directly
                RemoveEffects(Connection.ActiveChar, _moveType);

                if (!(_moveType is UnitMoveType mType))
                    return;
                
                _log.Debug("ActorFlags: 0x{0} - ClimbData: {1} - GcId: {2}", 
                    mType.ActorFlags.ToString("X"),
                    mType.ClimbData.ToString("X"), 
                    mType.GcId.ToString(("X")));
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
                        var stickyVerticalOffset = (float)(mType.ClimbData & 0x1FFF) / 8192f * 100f;
                        var stickyHorizontalOffset = (float)((mType.ClimbData & 0x001FE000) >> 13) / 256f * 100f;
                        _log.Debug("StickyPos - Vertical: {0}% , Horizontal: {1}%", stickyVerticalOffset,
                            stickyHorizontalOffset);
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
                
                Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(_objId, mType), false);
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
