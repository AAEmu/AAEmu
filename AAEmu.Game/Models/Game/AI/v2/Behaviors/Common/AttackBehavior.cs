using System;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AttackBehavior : BaseCombatBehavior
{
    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
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
            Ai.GoToReturn();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        Ai.Owner.StopMovement();
        Ai.Owner.IsInBattle = true;
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        PickSkillAndUseIt(SkillUseConditionKind.InCombat, Ai.Owner.CurrentTarget, targetDist);
    }

    public override void Exit()
    {
    }
}
