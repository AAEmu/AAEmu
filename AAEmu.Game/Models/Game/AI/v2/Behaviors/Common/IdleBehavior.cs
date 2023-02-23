using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class IdleBehavior : Behavior
    {
        public override void Enter()
        {
            Ai.Owner.InterruptSkills();
            Ai.Owner.StopMovement();
        }

        public override void Tick(TimeSpan delta)
        {
            if (!Ai.Owner.Template.Aggression)
                return; // Remove this if we need non-aggressive npcs to search for targetsegion.IsEmpty())

            var nearbyUnits = WorldManager.Instance.GetAround<Unit>(Ai.Owner, CheckSightRangeScale(10f));

            foreach (var unit in nearbyUnits)
            {
                if (Ai.Owner.Template.Aggression)
                {
                    // Need to check for stealth detection here..
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
                        var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, unit, true);
                        if (rangeOfUnit < CheckSightRangeScale(3f))
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

        public override void Exit()
        {
        }
    }
}
