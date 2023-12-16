using System;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AttackBehavior : BaseCombatBehavior
{
    public override void Enter()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc});
        }
    }

    public override void Tick(TimeSpan delta)
    {
        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.Owner.IsInBattle = false;
            Ai.GoToReturn();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        MoveInRange(Ai.Owner.CurrentTarget, delta);
        if (!CanUseSkill)
            return;

        Ai.Owner.IsInBattle = true;
        PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget);
    }

    public override void Exit()
    {
    }
}
