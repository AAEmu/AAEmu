using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// The movement back and forth across the Z-axis
    /// </summary>
    public class QuillZ : Patrol
    {
        public short Degree { get; set; } = 360;
        private float _maxVelocityForward = 4.5f;
        private float _maxVelocityBackward = -4.5f;

        /// <summary>
        /// QuillZ Z-axis movement
        /// </summary>
        /// <param name="gimmick"></param>
        public override void Execute(Gimmick gimmick)
        {
            //DeltaTime = 0.05f; // temporarily took a constant, later it will be necessary to take the current time
            var velocityX = gimmick.Vel.X;
            var velocityY = gimmick.Vel.Y;
            var velocityZ = gimmick.Vel.Z;
            
            vVelocity = gimmick.Vel;
            
            vPosition = new Vector3(gimmick.Position.X, gimmick.Position.Y, gimmick.Position.Z);

            if (vPosition.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0)
            {
                vTarget = new Vector3(gimmick.Position.X, gimmick.Position.Y, gimmick.Spawner.TopZ);
                vDistance = vTarget - vPosition; // dx, dy, dz
                //velocityZ += VelAccel * DeltaTime;
                //velocityZ = Math.Clamp(velocityZ, _maxVelocityBackward, _maxVelocityForward);
                velocityZ = _maxVelocityForward;

                MovingDistance = velocityZ * DeltaTime;

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
                //velocityZ -= VelAccel * DeltaTime;
                //velocityZ = Math.Clamp(velocityZ, _maxVelocityBackward, _maxVelocityForward);
                velocityZ = _maxVelocityBackward;
                MovingDistance = velocityZ * DeltaTime;

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
        public override void Execute(BaseUnit unit)
        {
            // Actor не будут двигаться по оси Z
            throw new NotImplementedException();
        }
        public override void Execute(Npc npc)
        {
            // Npc не будут двигаться по оси Z
            throw new NotImplementedException();
        }
        public override void Execute(Transfer transfer)
        {
            // Transfer не будет двигаться по оси Z
            throw new NotImplementedException();
        }
    }
}
