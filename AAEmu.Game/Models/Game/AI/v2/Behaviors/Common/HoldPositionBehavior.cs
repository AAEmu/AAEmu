using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class HoldPositionBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        _enter = true;
    }
    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        var delay = 150;
        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        if (!Ai.Owner.CheckInterval(delay))
        {
            Logger.Trace($"Skill: CooldownTime [{delay}]!");
        }
        else
        {
            var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
            PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, targetDist);
        }

        CheckAggression();
        CheckAlert();
        Ai.DoFollowDefaultNearestNpc();
    }

    public override void Exit()
    {
        _enter = false;
    }
}
