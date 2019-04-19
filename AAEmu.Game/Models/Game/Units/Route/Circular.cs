using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// 正圆形巡航路线
    /// 根据圆点进行正圆形路线行走，适合平面地区
    /// 非平整地区会造成NPC的遁地或飞空
    /// </summary>
    public class Circular:Patrol
    {
        /// <summary>
        /// 执行顺序编号
        /// 每次执行必须递增序号，否则重复序号的动作不被执行
        /// </summary>
        public static uint Seq = 0;
        public static uint Count = 0;

        public sbyte Radius { get; set; } = 5;
        public short Degree { get; set; } = 360;

        public override void Execute(Npc npc)
        {

            ++Count;
            ++Seq;

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
            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;
            moveType.Alertness = 2;
            moveType.Time = Seq;

            //圆形巡航
            var hudu = 2 * Math.PI / 360 * Count;
            moveType.X = npc.Spawner.Position.X + (float)Math.Sin(hudu) * Radius;
            moveType.Y = npc.Spawner.Position.Y + Radius - (float)Math.Cos(hudu) * Radius;

            //广播移动状态
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            ///如果执行次数小于角度则继续添加任务 否则停止移动
            if (Count < Degree)
                TaskManager.Instance.Schedule(
                           new UnitMove(this, npc), TimeSpan.FromMilliseconds(100)
                        );
            else
            {
                //停止移动
                moveType.DeltaMovement[1] = 0;
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                Count = 0;
            }
        }
    }
}
