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
        }

        public override void Tick(TimeSpan delta)
        {
            if (!Ai.Owner.Template.Aggression)
                return;//Remove this if we need non-aggressive npcs to search for targetsegion.IsEmpty())

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
                        var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, unit, true);
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

        public void OnEnemySeen(Unit target)
        {
            Ai.Owner.AddUnitAggro(NPChar.AggroKind.Damage, target, 1);
            Ai.GoToCombat();
        }

        public override void Exit()
        {
        }
    }
}
