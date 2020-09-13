using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Gimmicks;
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
    /// The regular square route is suitable for the plane area.
    /// 非平整地区会造成NPC的遁地或飞空
    /// Non-uniform areas can cause NPC refuge or flight
    /// </summary>
    public class Square : Patrol
    {
        public short VelZ { get; set; } = 0;
        public sbyte Radius { get; set; } = 5;
        public short Degree { get; set; } = 360;

        /// <summary>
        /// 正方形巡航 / Square Cruise
        /// </summary>
        /// <param name="unit"></param>
        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            var x = npc.Position.X;
            var y = npc.Position.Y;
            var z = npc.Position.Z;

            if (Count < Degree / 2)
            {
                npc.Position.X += (float)0.1;
            }
            else if (Count < Degree)
            {
                npc.Position.X -= (float)0.1;
            }

            if (Count < Degree / 4 || (Count > (Degree / 4 + Degree / 2) && Count < Degree))
            {
                npc.Position.Y += (float)0.1;
            }
            else if (Count < (Degree / 4 + Degree / 2))
            {
                npc.Position.Y -= (float)0.1;
            }

            // 模拟unit
            // Simulated unit
            moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

            // 改变NPC坐标
            // Change NPC coordinates
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            var tmpZ = npc.Position.Z; //просто инициализируем
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
                tmpZ = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
            }
            moveType.Z = tmpZ;

            // looks in the direction of movement
            var angle = MathUtil.CalculateAngleFrom(x, y, npc.Position.X, npc.Position.Y);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //AngleTmp = MathUtil.CalculateAngleFrom(x, y, npc.Position.X, npc.Position.Y);
            // slowly turn to the desired angle
            if (AngleTmp > 0)
            {
                Angle += AngVelocity;
                Angle = (float)Math.Clamp(Angle, 0f, AngleTmp);
            }
            else
            {
                Angle -= AngVelocity;
                Angle = (float)Math.Clamp(Angle, AngleTmp, 0f);
            }
            //var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
            vPosition = new Vector3(x, y, z);
            vTarget = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z);
            vDistance = vPosition - vTarget; // dx, dy, dz
            var direction = new Vector3();
            if (vDistance != Vector3.Zero)
            {
                direction = Vector3.Normalize(vDistance);
            }
            moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
            npc.Rot = moveType.Rot;

            moveType.DeltaMovement = new Vector3(0, 1.0f, 0);

            moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.

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
                //moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement = new Vector3();
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                //LoopAuto(npc);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(10, 25);
                TaskManager.Instance.Schedule(new UnitMovePause(this, npc), TimeSpan.FromSeconds(time));
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
