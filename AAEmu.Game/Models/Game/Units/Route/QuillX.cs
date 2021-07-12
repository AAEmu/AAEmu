using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// The movement back and forth across the X-axis
    /// </summary>
    public class QuillX : Patrol
    {
        public short Degree { get; set; } = 360;

        /// <summary>
        /// QuillX X-axis movement
        /// </summary>
        /// <param name="caster">Trigger role</param>
        /// <param name="npc">NPC</param>
        /// <param name="degree">Default angle 360 degrees</param>
        public override void Execute(Npc npc)
        {
            var x = npc.Transform.Local.Position.X;
            var y = npc.Transform.Local.Position.Y;

            if (Count < Degree / 2)
            {
                npc.Transform.Local.Translate(0.1f, 0f, 0f);
            }
            else if (Count < Degree)
            {
                npc.Transform.Local.Translate(-0.1f, 0f, 0f);
            }
            //    if (Count < Degree / 4 || (Count > (Degree / 4 + Degree / 2) && Count < Degree))
            //    {
            //        npc.Position.Y += (float)0.1;
            //    }
            //    else if (Count < (Degree / 4 + Degree / 2))
            //    {
            //        npc.Position.Y -= (float)0.1;
            //    }

            // Simulated unit
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            // Change NPC coordinates
            moveType.X = npc.Transform.Local.Position.X;
            moveType.Y = npc.Transform.Local.Position.Y;
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
                moveType.Z = WorldManager.Instance.GetHeight(npc.Transform);
            }

            var angle = MathUtil.CalculateAngleFrom(x, y, npc.Transform.Local.Position.X, npc.Transform.Local.Position.Y);
            var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;

            moveType.ActorFlags = 5;     // 5-walk, 4-run, 3-stand still
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127; // 88.. 118
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time += 50;    // has to change all the time for normal motion.

            // Broadcasting Mobile State
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Count < Degree)
            {
                Repeat(npc);
            }
            else
            {
                // Stop moving
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                //LoopAuto(npc);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(10, 25);
                TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
            }
        }
    }
}
