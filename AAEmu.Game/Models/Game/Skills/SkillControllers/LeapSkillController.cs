using System;
using System.Collections.Generic;
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
        private Point _endPosition;
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

            _endPosition = new Point();
            var angle = MathUtil.ConvertDegreeToDirection(MathUtil.CalculateAngleFrom(Owner.Position, Target.Position));
            (_endPosition.X, _endPosition.Y) = MathUtil.AddDistanceToFront(DistanceOffset / 1000f, target.Position.X, target.Position.Y, angle);
            _endPosition.Z = Target.Position.Z;

            var distance = MathUtil.CalculateDistance(Owner.Position, _endPosition, true);
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
            var targetDist = MathUtil.CalculateDistance(Owner.Position, _endPosition);
            if (targetDist <= 1.0f)
            {
                //TODO End Skill Controller
                End();
                return;
            }
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            var travelDist = Math.Min(targetDist, distance);
            var angle = MathUtil.CalculateAngleFrom(Owner.Position, _endPosition);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            var (newX, newY) = MathUtil.AddDistanceToFront(travelDist, Owner.Position.X, Owner.Position.Y, rotZ);
            var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, rotZ);

            Owner.Position.X = newX;
            Owner.Position.Y = newY;
            //TODO calculate z
            Owner.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(Owner.Position.ZoneId, Owner.Position.X, Owner.Position.Y) : Owner.Position.Z;
            Owner.Position.RotationZ = rotZ;

            moveType.X = Owner.Position.X;
            moveType.Y = Owner.Position.Y;
            moveType.Z = Owner.Position.Z;
            moveType.VelX = (short)velX;
            moveType.VelY = (short)velY;
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = Owner.Position.RotationZ;
            moveType.ActorFlags = flags;     // 5-walk, 4-run, 3-stand still
            moveType.Flags = 0x14;//SC move flag
            moveType.ScType = Template.Id;

            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = 127;
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = 0;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = 2; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.Now - DateTime.Today).TotalMilliseconds;

            Owner.SetPosition(Owner.Position);
            Owner.BroadcastPacket(new SCOneUnitMovementPacket(Owner.ObjId, moveType), false);
        }
    }
}
