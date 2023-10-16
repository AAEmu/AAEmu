using System;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class HoldPositionBehavior : Behavior
{
    public override void Enter()
    {
        Ai.Owner.StopMovement();
    }
    public override void Tick(TimeSpan delta)
    {
        PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner);
    }

    public override void Exit()
    {
    }
}
