using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// The movement back and forth across the Y-axis
    /// </summary>
    public class QuillZ : Patrol
    {
        public short Degree { get; set; } = 360;

        /// <summary>
        /// QuillZ Z-axis movement
        /// </summary>
        /// <param name="unit"></param>
        public override void Execute(BaseUnit unit)
        {
            if (!(unit is Gimmick gimmick)) { return; }

            // Change NPC coordinates
            if (Count <= Degree / 2)
            {
                gimmick.Position.Z += (float)0.179722222;
                gimmick.Vel = new Vector3(0f, 0f, 4.5f);
            }
            else if (Count > Degree / 2 && Count <= Degree)
            {
                gimmick.Position.Z -= (float)0.179722222;
                gimmick.Vel = new Vector3(0f, 0f, -4.5f);
            }
            gimmick.Time = Seq;    // has to change all the time for normal motion.
            gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Count < Degree)
            {
                Repeat(gimmick);
            }
            else
            {
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(20, 21);
                TaskManager.Instance.Schedule(new UnitMovePause(this, gimmick), TimeSpan.FromSeconds(time));
            }
        }
        public override void Execute(Transfer transfer)
        {
            throw new NotImplementedException();
        }
        public override void Execute(Gimmick gimmick)
        {
            if (gimmick == null) { return; }

            // Change NPC coordinates
            if (Count < Degree / 4 || Count > Degree / 4 + Degree / 2 && Count < Degree)
            {
                gimmick.Position.Z += (float)0.1;
            }
            else if (Count < Degree / 4 + Degree / 2)
            {
                gimmick.Position.Z -= (float)0.1;
            }
            gimmick.Time = Seq;    // has to change all the time for normal motion.
            gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Count < Degree)
            {
                Repeat(gimmick);
            }
            else
            {
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(20, 21);
                TaskManager.Instance.Schedule(new UnitMovePause(this, gimmick), TimeSpan.FromSeconds(time));
            }
        }
    }
}
