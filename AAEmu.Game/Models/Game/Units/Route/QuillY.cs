using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// The movement back and forth across the Y-axis
    /// </summary>
    public class QuillY : Patrol
    {
        // Quill - ткацкий челнок

        /// <summary>
        /// QuillX Н-axis movement
        /// </summary>
        /// <param name="unit"></param>
        public override void Execute(Npc npc)
        {
            if (npc == null) { return; }

            var x = npc.Position.X;
            var y = npc.Position.Y;
            var z = npc.Position.Z;

            var newz = npc.Position.Z; //просто инициализируем
            if (!(vEndPoint.X > 0))
            {
                float mov = (uint)Rand.Next(10, 25);
                var (newx, newy) = MathUtil.AddDistanceToFront(mov, npc.Position.X, npc.Position.Y, 0); // на север
                newz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, newx, newy) : npc.Position.Z;
                vEndPoint = new Vector3(newx, newy, newz);
                vBeginPoint = new Vector3(x, y, z);
            }

            if (Count < Degree / 4 || Count > Degree / 4 + Degree / 2 && Count < Degree)
            {
                vDistance = vEndPoint - vPosition; // dx, dy, dz
                vVelocity += vVelAccel * DeltaTime;
                vVelocity = Vector3.Clamp(vVelocity, vMaxVelocityBackWalk, vMaxVelocityForwardWalk);
                vMovingDistance = vVelocity * DeltaTime;

                if (Math.Abs(vDistance.X) >= Math.Abs(vMovingDistance.X) || Math.Abs(vDistance.Y) >= Math.Abs(vMovingDistance.Y))
                {
                    npc.Position.X += vMovingDistance.X;
                    npc.Position.Y += vMovingDistance.Y;
                    npc.Position.Z += vMovingDistance.Z;
                    npc.Vel = vVelocity;
                }
                else
                {
                    npc.Position.X = vTarget.X;
                    npc.Vel = Vector3.Zero;
                }
            }
            else if (Count < Degree / 4 + Degree / 2)
            {
                vDistance = vBeginPoint - vPosition; // dx, dy, dz
                vVelocity -= vVelAccel * DeltaTime;
                vVelocity = Vector3.Clamp(vVelocity, vMaxVelocityBackWalk, vMaxVelocityForwardWalk);
                vMovingDistance = vVelocity * DeltaTime;

                if (Math.Abs(vDistance.X) >= Math.Abs(vMovingDistance.X) || Math.Abs(vDistance.Y) >= Math.Abs(vMovingDistance.Y))
                {
                    npc.Position.X += vMovingDistance.X;
                    npc.Position.Y += vMovingDistance.Y;
                    npc.Position.Z += vMovingDistance.Z;
                    npc.Vel = vVelocity;
                }
                else
                {
                    npc.Position.X = vTarget.X;
                    npc.Position.X += vMovingDistance.X;
                    npc.Position.Y += vMovingDistance.Y;
                    npc.Position.Z += vMovingDistance.Z;
                    npc.Vel = Vector3.Zero;
                }
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
            AngleTmp = MathUtil.CalculateAngleFrom(x, y, npc.Position.X, npc.Position.Y);
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
            var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
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
