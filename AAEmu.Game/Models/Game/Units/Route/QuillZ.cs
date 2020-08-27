using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// The movement back and forth across the Z-axis
    /// </summary>
    public class QuillZ : Patrol
    {
        public short Degree { get; set; } = 360;

        private float maxVelocityForward = 4.5f;
        private float maxVelocityBackward = -4.5f;
        private float velAccel = 1f;
        public Vector3 vPosition;
        public Vector3 vTarget;
        public Vector3 vDistance;


        /// <summary>
        /// QuillZ Z-axis movement
        /// </summary>
        /// <param name="unit"></param>
        public override void Execute(BaseUnit unit)
        {
            // Npc не будут двигаться по оси Z
            throw new NotImplementedException();
        }
        public override void Execute(Transfer transfer)
        {
            // Transfer не будет двигаться по оси Z
            throw new NotImplementedException();
        }
        public override void Execute(Gimmick gimmick)
        {
            var deltaTime = 0.05f; // temporarily took a constant, later it will be necessary to take the current time
            var velocityX = gimmick.Vel.X;
            var velocityY = gimmick.Vel.Y;
            var velocityZ = gimmick.Vel.Z;
            vPosition = new Vector3(gimmick.Position.X, gimmick.Position.Y, gimmick.Position.Z);

            if (vPosition.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0)
            {
                vTarget = new Vector3(gimmick.Position.X, gimmick.Position.Y, gimmick.Spawner.TopZ);
                vDistance = vTarget - vPosition; // dx, dy, dz
                velocityZ += velAccel * deltaTime;
                velocityZ = Math.Clamp(velocityZ, maxVelocityBackward, maxVelocityForward);
                var MovingDistance = velocityZ * deltaTime;

                if (Math.Abs(vDistance.Z) >= Math.Abs(MovingDistance))
                {
                    gimmick.Position.Z += MovingDistance;
                    gimmick.Vel = new Vector3(velocityX, velocityY, velocityZ);
                }
                else
                {
                    gimmick.Position.Z = vTarget.Z;
                    gimmick.Vel = new Vector3(0f, 0f, 0f);
                }
            }
            else // if (vPosition.Z > gimmick.Spawner.BottomZ && gimmick.Vel.Z < 0)
            {
                vTarget = new Vector3(gimmick.Position.X, gimmick.Position.Y, gimmick.Spawner.BottomZ);
                vDistance = vTarget - vPosition; // dx, dy, dz
                velocityZ -= velAccel * deltaTime;
                velocityZ = Math.Clamp(velocityZ, maxVelocityBackward, maxVelocityForward);
                var MovingDistance = velocityZ * deltaTime;

                if (Math.Abs(vDistance.Z) >= Math.Abs(MovingDistance))
                {
                    gimmick.Position.Z += MovingDistance;
                    gimmick.Vel = new Vector3(velocityX, velocityY, velocityZ);
                }
                else
                {
                    gimmick.Position.Z = vTarget.Z;
                    gimmick.Vel = new Vector3(0f, 0f, 0f);
                }
            }

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Math.Abs(gimmick.Vel.Z) > 0)
            {
                gimmick.Time = Seq;    // has to change all the time for normal motion.
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
                Repeat(gimmick);
            }
            else
            {
                // остановиться внизу и  вверху на time секунд
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
                TaskManager.Instance.Schedule(new UnitMovePause(this, gimmick), TimeSpan.FromSeconds(gimmick.Spawner.WaitTime));
            }
        }
    }
}
