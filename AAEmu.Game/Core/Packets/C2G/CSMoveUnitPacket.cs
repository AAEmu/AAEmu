using System.Collections.Generic;
using System.Linq;
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
            // TODO: We can probably rewrite this
            if (_objId != Connection.ActiveChar.ObjId) // Can be mate
            {
                switch (_moveType)
                {
                    case ShipRequestMoveType srmt:
                    {
                        // TODO : Get by ObjId
                        var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
                        if (slave == null)
                            return;

                        slave.ThrottleRequest = srmt.Throttle;
                        slave.SteeringRequest = srmt.Steering;
                        break;
                    }
                    case VehicleMoveType vmt:
                    {
                        var (rotDegX, rotDegY, rotDegZ) =
                            MathUtil.GetSlaveRotationInDegrees(vmt.RotationX, vmt.RotationY, vmt.RotationZ);
                        var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationFromDegrees(rotDegX, rotDegY, rotDegZ);

                        var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
                        if (slave == null)
                            return;
                    
                        slave.SetPosition(vmt.X, vmt.Y, vmt.Z, MathUtil.ConvertRadianToDirection(rotDegX), MathUtil.ConvertRadianToDirection(rotDegY), MathUtil.ConvertRadianToDirection(rotDegZ));
                        slave.BroadcastPacket(new SCOneUnitMovementPacket(_objId, vmt), true);
                        break;
                    }
                    // TODO : check target has Telekinesis buff
                    case UnitMoveType dmt:
                    {
                        var unit = WorldManager.Instance.GetUnit(_objId);
                        if (unit == null)
                            break;
                        unit.SetPosition(dmt.X, dmt.Y, dmt.Z);
                        unit.BroadcastPacket(new SCOneUnitMovementPacket(_objId, dmt), true);
                        break;
                    }
                }

                var mateInfo = MateManager.Instance.GetActiveMateByMateObjId(_objId);
                if (mateInfo == null) return;

                RemoveEffects(mateInfo, _moveType);
                mateInfo.SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);
                foreach (var passenger in mateInfo.GetPassengers())
                    RemoveEffects(passenger, _moveType);

                // TODO: mount fall dmg
            }
            else
            {
                RemoveEffects(Connection.ActiveChar, _moveType);

                if (!(_moveType is UnitMoveType mType))
                    return;
                
                if ((mType.ActorFlags & 0x20) != 0)
                {
                    Connection
                        .ActiveChar
                        .SetPosition(mType.X2, mType.Y2, mType.Z2, mType.RotationX, mType.RotationY, mType.RotationZ);

                    //do we need it for boats?
                    if (mType.FallVel > 0)
                        Connection.ActiveChar.DoFallDamage(mType.FallVel);
                }
                else
                {
                    Connection
                        .ActiveChar
                        .SetPosition(_moveType.X, _moveType.Y, _moveType.Z, _moveType.RotationX, _moveType.RotationY, _moveType.RotationZ);

                    if (mType.FallVel > 0)
                        Connection.ActiveChar.DoFallDamage(mType.FallVel);
                }
                
                Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(_objId, _moveType), false);
            }
        }

        private static void RemoveEffects(BaseUnit unit, MoveType moveType)
        {
            if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
                unit.Buffs.TriggerRemoveOn(BuffRemoveOn.Move);
        }
    }
}
