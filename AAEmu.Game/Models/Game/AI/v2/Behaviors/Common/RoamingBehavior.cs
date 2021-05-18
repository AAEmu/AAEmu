using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class RoamingBehavior : Behavior
    {
        private Point _targetRoamPosition;
        private DateTime _nextRoaming;
        
        public override void Enter()
        {
            UpdateRoaming();
        }

        public override void Tick(TimeSpan delta)
        {
            if (Ai.Owner.Template.Aggression)
            {
                var nearbyUnits = WorldManager.Instance.GetAround<Unit>(Ai.Owner, 10 * Ai.Owner.Template.SightRangeScale);

                foreach (var unit in nearbyUnits)
                {
                    if (Ai.Owner.Template.Aggression)
                    {
                        //Need to check for stealth detection here..
                        if (Ai.Owner.Template.SightFovScale >= 2.0f || MathUtil.IsFront(Ai.Owner, unit))
                        {
                            if (Ai.Owner.CanAttack(unit))
                            {
                                OnEnemySeen(unit);
                                break;
                            }
                        }
                        else
                        {
                            var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner.Position, unit.Position, true);
                            if (rangeOfUnit < 3 * Ai.Owner.Template.SightRangeScale)
                            {
                                if (Ai.Owner.CanAttack(unit))
                                {
                                    OnEnemySeen(unit);
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (_targetRoamPosition == null && DateTime.Now > _nextRoaming)
                UpdateRoaming();
            
            if (_targetRoamPosition == null)
                return;
            
            Ai.Owner.MoveTowards(_targetRoamPosition, 1.8f * (delta.Milliseconds / 1000.0f), 5);
            var dist = MathUtil.CalculateDistance(Ai.Owner.Position, _targetRoamPosition);
            if (dist < 1.0f)
            {
                Ai.Owner.StopMovement();
                _targetRoamPosition = null;
                _nextRoaming = DateTime.Now.AddSeconds(3); // Rand 3-6 would look nice ?
            }
        }

        public override void Exit()
        {
        }

        private void UpdateRoaming()
        {
            // TODO : Group member handling
            
            _targetRoamPosition = AIUtils.CalcNextRoamingPosition(Ai);
        }

        public void OnEnemySeen(Unit target)
        {
            Ai.Owner.AddUnitAggro(NPChar.AggroKind.Damage, target, 1);
            Ai.GoToCombat();
        }
    }
}
