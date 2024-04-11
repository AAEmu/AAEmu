using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlertBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();

        CheckPipeName();
        Ai.Owner.StopMovement();

        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.InAlert(this, new InAlertArgs { Npc = npc });
        }

        _pipeName = "phase_dragon_ground";
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (!UpdateTarget() || ShouldReturn) // проверим, что таблица abuser не пустая и назначим текущую цель
        {
            Ai.GoToReturn();
            //Ai.GoToRunCommandSet();
            return;
        }

        if (Ai.Owner.CurrentTarget == null)
            return;

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        if (!CanUseSkill)
            return;

        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        if (!Ai.Owner.CheckInterval(Delay)) { return; }
        var targetDist = Ai.Owner.GetDistanceTo(Ai.Owner.CurrentTarget);
        PickSkillAndUseIt(SkillUseConditionKind.OnAlert, Ai.Owner.CurrentTarget, targetDist); // используем скиллы на врага
    }

    public override void Exit()
    {
        _enter = false;
    }
}
