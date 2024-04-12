using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class HoldPositionBehavior : BaseCombatBehavior
{
    private bool _enter;
    private float teleportThresholdDist = 7f;
    private float returnThresholdDist = 3f;

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

        CheckAggression(); // наполним таблицу abuser (возможно, что уйдем с этого поведения на другое)

        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        if (!Ai.Owner.CheckInterval(Delay)) { return; }
        PickSkillAndUseIt(SkillUseConditionKind.InIdle, Ai.Owner, 0); // используем скиллы на себя
    }

    public override void Exit()
    {
        _enter = false;
    }
}
