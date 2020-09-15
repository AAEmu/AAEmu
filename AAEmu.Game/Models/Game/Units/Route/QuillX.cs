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
    /// The movement back and forth across the X-axis
    /// </summary>
    public class QuillX : Patrol
    {
        private bool move;
        private Vector3 newPosition;
        private Vector3 diff;
        private float diffX;
        private float diffY;
        private float newx;
        private float newy;
        private float newz;
        private float x;
        private float y;
        private float z;
        private sbyte rotZ;

        /// <summary>
        /// QuillX X-axis movement
        /// </summary>
        /// <param name="npc"></param>
        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            move = false;
            x = npc.Position.X;
            y = npc.Position.Y;
            z = npc.Position.Z;
            vPosition = new Vector3(x, y, z);

            if (!InPatrol)
            {
                var mov = (float)Rand.Next(2, 5);
                rotZ = (sbyte)Rand.Next(0, 127);
                (newx, newy) = MathUtil.AddDistanceToFront(mov, x, y, rotZ); // 0 на север
                newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, newx, newy) : npc.Position.Z;
                vEndPoint = new Vector3(newx, newy, newz);
                vBeginPoint = new Vector3(x, y, z);
                Distance = MathUtil.GetDistance(vBeginPoint, vEndPoint);
                InPatrol = true;
            }
            if (!GoBack)
            {
                //Time += DeltaTime;
                //Time = Math.Clamp(Time, 0, 1);
                //newPosition = Vector3.Lerp(vBeginPoint, vEndPoint, Time);
                //diffX = newPosition.X - npc.Position.X;
                //diffY = newPosition.Y - npc.Position.Y;
                //npc.Position.X = newPosition.X;
                //npc.Position.Y = newPosition.Y;
                //if (Math.Abs(diffX) > 0 || Math.Abs(diffY) > 0) { move = true; }
                //if (Math.Abs(vDistance.X) < diffX) { npc.Position.X = vEndPoint.X; }
                //if (Math.Abs(vDistance.Y) < diffY) { npc.Position.Y = vEndPoint.Y; }
                //if (!(Math.Abs(diffX) > 0) && !(Math.Abs(diffY) > 0))
                //{
                //    GoBack = true;
                //    move = false;
                //    Time = 0f;
                //}
                //Angle = MathUtil.CalculateAngleFrom(vBeginPoint.X, vBeginPoint.Y, vEndPoint.X, vEndPoint.Y);

                vDistance = vEndPoint - vPosition;
                // скорость движения velociti за период времени DeltaTime
                var velociti = 4.5f * DeltaTime;
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                var direction = vEndPoint - vPosition;
                // вектор направления необходимо нормализовать
                if (direction != Vector3.Zero)
                {
                    direction = Vector3.Normalize(direction);
                }
                
                // вектор скорости (т.е. координаты, куда попадем двигаясь со скоростью velociti по направдению direction)
                diff = direction * velociti;

                npc.Position.X += diff.X;
                npc.Position.Y += diff.Y;
                if (Math.Abs(diff.X) > 0 || Math.Abs(diff.Y) > 0) { move = true; }
                if (Math.Abs(vDistance.X) < Math.Abs(diff.X)) { npc.Position.X = vEndPoint.X; }
                if (Math.Abs(vDistance.Y) < Math.Abs(diff.Y)) { npc.Position.Y = vEndPoint.Y; }
                if (!(Math.Abs(diff.X) > 0) && !(Math.Abs(diff.Y) > 0))
                {
                    GoBack = true;
                    move = false;
                    Time = 0f;
                }
                Angle = MathUtil.CalculateAngleFrom(vBeginPoint.X, vBeginPoint.Y, vEndPoint.X, vEndPoint.Y);
            }
            else
            {
                //Time += DeltaTime;
                //Time = Math.Clamp(Time, 0, 1);
                //newPosition = Vector3.Lerp(vEndPoint, vBeginPoint, Time);
                //diffX = newPosition.X - npc.Position.X;
                //diffY = newPosition.Y - npc.Position.Y;
                //npc.Position.X = newPosition.X;
                //npc.Position.Y = newPosition.Y;
                //if (Math.Abs(diffX) > 0 || Math.Abs(diffY) > 0) { move = true; }
                //if (Math.Abs(vDistance.X) < diffX) { npc.Position.X = vBeginPoint.X; }
                //if (Math.Abs(vDistance.Y) < diffY) { npc.Position.Y = vBeginPoint.Y; }
                //Angle = MathUtil.CalculateAngleFrom(vEndPoint.X, vEndPoint.Y, vBeginPoint.X, vBeginPoint.Y);
                ////Angle = MathUtil.DegreeToRadian(Angle);
                //if (!(Math.Abs(diffX) > 0) && !(Math.Abs(diffY) > 0))
                //{
                //    GoBack = false;
                //    move = false;
                //    Time = 0f;
                //    InPatrol = false;
                //}
                vDistance = vBeginPoint - vPosition;
                // скорость движения velociti за период времени DeltaTime
                var velociti = 4.5f * DeltaTime;
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                var direction = vBeginPoint - vPosition;
                // вектор направления необходимо нормализовать
                if (direction != Vector3.Zero)
                {
                    direction = Vector3.Normalize(direction);
                }

                // вектор скорости (т.е. координаты, куда попадем двигаясь со скоростью velociti по направдению direction)
                diff = direction * velociti;

                npc.Position.X += diff.X;
                npc.Position.Y += diff.Y;
                if (Math.Abs(diff.X) > 0 || Math.Abs(diff.Y) > 0) { move = true; }
                if (Math.Abs(vDistance.X) < Math.Abs(diff.X)) { npc.Position.X = vBeginPoint.X; }
                if (Math.Abs(vDistance.Y) < Math.Abs(diff.Y)) { npc.Position.Y = vBeginPoint.Y; }
                if (!(Math.Abs(diff.X) > 0) && !(Math.Abs(diff.Y) > 0))
                {
                    GoBack = false;
                    move = false;
                    Time = 0f;
                    InPatrol = false;
                }
                Angle = MathUtil.CalculateAngleFrom(vEndPoint.X, vEndPoint.Y, vBeginPoint.X, vBeginPoint.Y);
            }
            // Simulated unit
            moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
            // Change NPC coordinates
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
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
                newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
            }
            moveType.Z = newz;
            // looks in the direction of movement
            rotZ = MathUtil.ConvertDegreeToDirection(Angle);
            moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
            npc.Rot = moveType.Rot;

            //moveType.DeltaMovement = new Vector3(0, 127, 0);
            moveType.DeltaMovement = new Vector3(0, 1.0f, 0);
            //moveType.DeltaMovement = new Vector3(diff.X, diff.Y, diff.Z);

            moveType.Flags = 5;     // 5-walk, 4-run, 3-stand still
            moveType.Stance = 1;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 0; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = Seq;    // has to change all the time for normal motion.
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (move)
            {
                Repeat(npc);
            }
            else
            {
                // Stop moving
                moveType.DeltaMovement = new Vector3();
                moveType.Velocity = new Vector3();
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                // остановиться в вершине на time секунд
                double time = (uint)Rand.Next(5, 15);
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
