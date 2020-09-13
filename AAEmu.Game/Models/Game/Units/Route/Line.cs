using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    internal class Line : Patrol
    {
        public Point LastPatrolPosition { get; set; }

        private bool move;
        private float returnDistance;
        private float absoluteReturnDistance;
        private Vector3 newPosition;
        private float diffX;
        private float diffY;
        private float diffZ;

        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            if (LastPatrolPosition == null)
            {
                Stop(npc);
                return;
            }

            move = false;
            vPosition = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            Time += DeltaTime;
            Time = Math.Clamp(Time, 0, 1);

            if (!InPatrol)
            {
                vPausePosition = Vector3.Zero;
                vBeginPoint = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
                vEndPoint = new Vector3(LastPatrolPosition.X, LastPatrolPosition.Y, LastPatrolPosition.Z);
                Angle = MathUtil.CalculateAngleFrom(vBeginPoint.X, vBeginPoint.Y, vEndPoint.X, vEndPoint.Y);
                Angle = MathUtil.DegreeToRadian(Angle);
                InPatrol = true;
                npc.IsInBattle = false;
            }
            vDistance = vPosition - vEndPoint; // dx, dy, dz
            // accelerate to maximum speed
            npc.Vel = vMaxVelocityForwardWalk;
            vVelocity = vMaxVelocityForwardWalk;

            // find out how far we have traveled over the past period of time
            newPosition = Vector3.Lerp(vBeginPoint, vEndPoint, Time);
            diffX = newPosition.X - npc.Position.X;
            diffY = newPosition.Y - npc.Position.Y;

            npc.Position.X = newPosition.X;
            npc.Position.Y = newPosition.Y;

            if (Math.Abs(diffX) > 0 || Math.Abs(diffY) > 0) { move = true; }
            if (Math.Abs(vDistance.X) < diffX) { npc.Position.X = vTarget.X; }
            if (Math.Abs(vDistance.Y) < diffY) { npc.Position.Y = vTarget.Y; }

            // 模拟unit
            // simulation unit
            moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
            // 改变NPC坐标
            // Change the NPC coordinates
            moveType.X = newPosition.X;
            moveType.Y = newPosition.Y;
            if (npc.TemplateId == 13677 || npc.TemplateId == 13676) // swimming
            {
                moveType.Z = 98.5993f;
            }
            else if (npc.TemplateId == 13680) // shark
            {
                moveType.Z = 95.5993f;
            }
            else // other
            {
                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
            }
            diffZ = moveType.Z - npc.Position.Z;
            npc.Position.Z = moveType.Z;
            // looks in the direction of movement
            moveType.Rot = new Quaternion(0f, 0f, (float)Angle, 1f);
            npc.Rot = moveType.Rot;
            moveType.DeltaMovement = new Vector3(0, 1.0f, 0);
            moveType.Flags = 5;     // 5-walk, 4-run, 3-stand still
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.

            if (move)
            {
                // 广播移动状态
                // broadcast mobile status
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopDelay = 500;
                Repeat(npc);
            }
            else
            {
                // 停止移动
                // stop moving
                moveType.DeltaMovement = new Vector3();
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                LoopAuto(npc);
            }
        }
        public override void Execute(Transfer transfer)
        {
            throw new NotImplementedException();
        }
        public override void Execute(Gimmick gimmick)
        {
            throw new NotImplementedException();
        }
        public override void Execute(BaseUnit unit)
        {
            throw new NotImplementedException();
        }
    }
}
