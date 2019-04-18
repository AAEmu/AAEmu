using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    /// <summary>
    /// 巡逻
    /// 指在指定区域内或指定线路上进行循环移动
    /// </summary>
    public class Patrol
    {
        public static uint time = 0;
        public static uint count = 0;
        public static short VelZ = 0;
        /// <summary>
        /// 执行巡逻任务
        /// </summary>
        /// <param name="caster"></param>
        public void Apply(Unit caster, Npc npc)
        {

            ++count;
            ++time;

            uint s = 360;

            //正方形巡航

            //if (count < s/2)
            //{
            //    npc.Position.X += (float)0.1;
            //}
            //else if (count < s)
            //{
            //    npc.Position.X -= (float)0.1;
            //}

            //if (count < s/4 || (count > (s / 4 + s / 2) && count < s))
            //{
            //    npc.Position.Y += (float)0.1;
            //}
            //else if (count < (s / 4 + s / 2))
            //{
            //    npc.Position.Y -= (float)0.1;
            //}

          

            if (VelZ > 127)
            {
                VelZ = 0;
            }
            else
            {
                VelZ += (sbyte)0.6299;
            }


            //debug by Yanlongli date 2019.04.18
            //将自己的移动赋予选择的对象 跟随自己一起移动
            //模拟unit
            var type = (MoveTypeEnum)1;
            //返回moveType对象
            var moveType = (UnitMoveType)MoveType.GetType(type);

            //改变NPC坐标
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            moveType.Z = npc.Position.Z;
            moveType.Flags = 5;
            moveType.VelZ = VelZ;
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;
            moveType.Alertness = 2;
            moveType.Time = time;

            //圆形巡航
            
            var r = 5;
            //degree = 0;
            //degree++;
            var hudu = 2 * Math.PI / 360 * count;
            moveType.X = npc.Position.X + (float)Math.Sin(hudu) * r;
            moveType.Y = npc.Position.Y+r - (float)Math.Cos(hudu) * r;


            caster.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);

            if (count < s)
                TaskManager.Instance.Schedule(
                           new UnitMove(new Patrol(), caster, npc), TimeSpan.FromMilliseconds(100)
                        );
            else
            {
                moveType.DeltaMovement[1] = 0;
                caster.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                count = 0;
            }
               

        }
    }
}
