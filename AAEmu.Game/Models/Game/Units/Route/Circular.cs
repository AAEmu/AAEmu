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
    /// 正圆形巡航路线
    /// Round cruise route
    /// 根据圆点进行正圆形路线行走，适合平面地区
    /// According to the circular point, the regular circular route is suitable for the plane area.
    /// 非平整地区会造成NPC的遁地或飞空
    /// Non-flat areas will cause NPC's depression or airborne
    /// </summary>
    public class Circular : Patrol
    {
        public short VelZ { get; set; } = 0;
        public sbyte Radius { get; set; } = 2; //5;
        public short Degree { get; set; } = 180;

        public override void Execute(Npc npc)
        {
            var x = npc.Position.X;
            var y = npc.Position.Y;
            // debug by Yanlongli date 2019.04.18
            // 将自己的移动赋予选择的对象 跟随自己一起移动
            // Give your own movement to the selected object, move with yourself
            // 模拟unit
            // Simulated unit
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            if (npc.Position.RotationX < 127)
            {
                npc.Position.RotationZ += 1;
            }
            else
            {
                npc.Position.RotationX = 0;
            }

            // 改变NPC坐标
            // Changing NPC coordinates
            moveType.Flags = 5;     // 5-walk, 4-run, 3-stand still
            //moveType.VelZ = VelZ;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127; // 88.. 118
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.

            // 圆形巡航
            // Round cruising
            var hudu = 4 * Math.PI / 360 * Count;
            moveType.X = npc.Position.X = npc.Spawner.Position.X + (float)Math.Sin(hudu) * Radius;
            moveType.Y = npc.Position.Y = npc.Spawner.Position.Y + Radius - (float)Math.Cos(hudu) * Radius;

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
            var angle = MathUtil.CalculateAngleFrom(x, y, npc.Position.X, npc.Position.Y);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;

            // 广播移动状态
            // Broadcasting Mobile State
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            // 如果执行次数小于角度则继续添加任务 否则停止移动
            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Count < Degree)
            {
                Repeat(npc);
            }
            else
            {
                // 停止移动
                // Stop moving
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                //LoopAuto(npc);
                // stop at the top for time seconds
                double time = (uint)Rand.Next(10, 25);
                TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
            }
        }
    }
}
