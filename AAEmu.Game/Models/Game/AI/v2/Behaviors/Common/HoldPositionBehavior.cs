using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class HoldPositionBehavior : Behavior
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

        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, targetDist);
        CheckAggression();
    }

    public override void Exit()
    {
        _enter = false;
    }
}
