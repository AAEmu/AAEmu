using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers
{
    public class LeapSkillController : SkillController
    {
        public int Angle { get; set; }
        public int Speed { get; set; }
        public int Duration { get; set; }
        public int DistanceOffset { get; set; }

        private float _calculatedSpeed;
        private Vector3 _endPosition;
        public enum LeapDirection
        {
            Both = 0,
            ForwardOnly = 1,
            BackwardOnly = 2
        }
        public LeapDirection Direction { get; set; }


        public LeapSkillController(SkillControllerTemplate template, Unit owner, Unit target)
        {
            Template = template;
            Owner = owner;
            Target = target;

            Angle = template.Value[0];
            Speed = template.Value[1];
            Duration = template.Value[2];
            DistanceOffset = template.Value[3];
            Direction = (LeapDirection)template.Value[6];

            var angle = (float)MathUtil.CalculateAngleFrom(owner, target);
            (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(DistanceOffset / 1000f, target.Transform.World.Position.X, target.Transform.World.Position.Y, angle);
            _endPosition.Z = Target.Transform.World.Position.Z;

            var distance = MathUtil.CalculateDistance(Owner.Transform.World.Position, _endPosition, true);
            _calculatedSpeed = distance / (Duration / 1000f);

        }

        public void Tick(TimeSpan delta)
        {
            if (Owner.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun || e.Template.Sleep) || Owner.IsDead)
            {
                End();
                return;
            };
            MoveTowards(_calculatedSpeed * (float)(delta.TotalMilliseconds/1000f));
        }

        public override void Execute()
        {
            base.Execute();
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
        }

        public override void End()
        {
            base.End();
            TickManager.Instance.OnTick.UnSubscribe(Tick);
        }

        public void MoveTowards(float distance, byte flags = 4)
        {
            var targetDist = MathUtil.CalculateDistance(Owner.Transform.World.Position, _endPosition);
            if (targetDist <= 1.0f)
            {
                //TODO End Skill Controller
                End();
                return;
            }

            var oldPosition = Owner.Transform.World.ClonePosition();

            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            var travelDist = Math.Min(targetDist, distance);
            var angle = (float)MathUtil.CalculateAngleFrom(Owner.Transform.World.Position, _endPosition);
            //var rotZ = MathUtil.ConvertDegreeToSByteDirection(angle);
            var (newX, newY) = MathUtil.AddDistanceToFront(travelDist, Owner.Transform.World.Position.X, Owner.Transform.World.Position.Y, angle);
            var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, angle);
            var newZ = AppConfiguration.Instance.HeightMapsEnable ? 
                WorldManager.Instance.GetHeight(Owner.Transform.ZoneId, Owner.Transform.World.Position.X, Owner.Transform.World.Position.Y) : 
                Owner.Transform.World.Position.Z;

            // TODO: Implement Transform.World
            Owner.Transform.World.SetPosition(newX,newY, newZ);
            Owner.Transform.World.SetRotationDegree(0f, 0f, angle-90);



            moveType.X = Owner.Transform.Local.Position.X;
            moveType.Y = Owner.Transform.Local.Position.Y;
            moveType.Z = Owner.Transform.Local.Position.Z;
            moveType.VelX = (short)velX;
            moveType.VelY = (short)velY;
            var rpy = Owner.Transform.Local.ToRollPitchYawSBytesMovement();
            moveType.RotationX = 0; //rpy.Item1;
            moveType.RotationY = 0; //rpy.Item2;
            moveType.RotationZ = rpy.Item3;
            moveType.ActorFlags = flags;     // 5-walk, 4-run, 3-stand still
            moveType.Flags = 0x14;//SC move flag
            moveType.ScType = Template.Id;

            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

            Owner.CheckMovedPosition(oldPosition);
            //Owner.SetPosition(Owner.Position);
            Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
        }
    }
}
