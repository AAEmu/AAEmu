﻿using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSMoveUnitPacket : GamePacket
    {
        public CSMoveUnitPacket() : base(0x089, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var myObjId = Connection.ActiveChar.ObjId;
            var type = (MoveTypeEnum)stream.ReadByte();
            var moveType = MoveType.GetType(type);
            stream.Read(moveType);

            if (objId != myObjId) // Can be mate
            {
                if (moveType is ShipRequestMoveType srmt)
                {
                    var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(myObjId);
                    if (slave == null)
                        return;

                    slave.ThrottleRequest = srmt.Throttle;
                    slave.SteeringRequest = srmt.Steering;
                }
                
                if (moveType is VehicleMoveType vmt)
                {
                    var quatX = vmt.RotationX * 0.00003052f;
                    var quatY = vmt.RotationY * 0.00003052f;
                    var quatZ = vmt.RotationZ * 0.00003052f;
                    var quatNorm = quatX * quatX + quatY * quatY + quatZ * quatZ;

                    var quatW = 0.0f;
                    if (quatNorm < 0.99750)
                    {
                        quatW = (float)Math.Sqrt(1.0 - quatNorm);
                    }

                    var quat = new Quaternion(quatX, quatY, quatZ, quatW);

                    var roll = (float)Math.Atan2(2 * (quat.W * quat.X + quat.Y * quat.Z),
                        1 - 2 * (quat.X * quat.X + quat.Y * quat.Y));
                    var sinp = 2 * (quat.W * quat.Y - quat.Z * quat.X);
                    var pitch = 0.0f;
                    if (Math.Abs(sinp) >= 1)
                        pitch = (float)Math.CopySign(Math.PI / 2, sinp);
                    else
                    {
                        pitch = (float)Math.Asin(sinp);
                    }

                    var yaw = (float)Math.Atan2(2 * (quat.W * quat.Z + quat.X * quat.Y), 1 - 2 * (quat.Y * quat.Y + quat.Z * quat.Z));

                    var reverseQuat = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
                    var reverseZ = reverseQuat.Y / 0.00003052f;
                    
                    // Connection.ActiveChar.SendMessage("Client: " + vmt.RotationZ + ". Yaw (deg): " + (yaw * 180 / Math.PI) + ". Reverse: " + reverseZ);
                }
                
                var mateInfo = MateManager.Instance.GetActiveMateByMateObjId(objId);
                if (mateInfo == null) return;

                RemoveEffects(mateInfo, moveType);
                mateInfo.SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
                mateInfo.BroadcastPacket(new SCOneUnitMovementPacket(objId, moveType), false);

                if (mateInfo.Att1 > 0)
                {

                    var owner = WorldManager.Instance.GetCharacterByObjId(mateInfo.Att1);
                    if (owner != null)
                    {
                        RemoveEffects(owner, moveType);
                        owner.SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
                        owner.BroadcastPacket(new SCOneUnitMovementPacket(owner.ObjId, moveType), false);
                    }
                }

                if (mateInfo.Att2 > 0)
                {
                    var passenger = WorldManager.Instance.GetCharacterByObjId(mateInfo.Att2);
                    if (passenger != null)
                    {
                        RemoveEffects(passenger, moveType);
                        passenger.SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
                        passenger.BroadcastPacket(new SCOneUnitMovementPacket(passenger.ObjId, moveType), false);
                    }
                }
            }
            else
            {
                RemoveEffects(Connection.ActiveChar, moveType);

                // This will allow you to walk on a boat, but crashes other clients. Not sure why yet.
                if (moveType is UnitMoveType mType && (mType.ActorFlags & 0x20) != 0)
                {
                    Connection
                        .ActiveChar
                        .SetPosition(mType.X2 + mType.X, mType.Y2 + mType.Y, mType.Z2 + mType.Z, mType.RotationX, mType.RotationY, mType.RotationZ);
                }
                else
                {
                    Connection
                        .ActiveChar
                        .SetPosition(moveType.X, moveType.Y, moveType.Z, moveType.RotationX, moveType.RotationY, moveType.RotationZ);
                    
                }

                
                Connection.ActiveChar.BroadcastPacket(new SCOneUnitMovementPacket(objId, moveType), false);
            }
        }

        private static void RemoveEffects(BaseUnit unit, MoveType moveType)
        {
            if (moveType.VelX != 0 || moveType.VelY != 0 || moveType.VelZ != 0)
            {
                var effects = unit.Effects.GetEffectsByType(typeof(BuffTemplate));
                foreach (var effect in effects)
                    if (((BuffTemplate)effect.Template).RemoveOnMove)
                        effect.Exit();
                effects = unit.Effects.GetEffectsByType(typeof(BuffEffect));
                foreach (var effect in effects)
                    if (((BuffEffect)effect.Template).Buff.RemoveOnMove)
                        effect.Exit();
            }
        }
    }
}
