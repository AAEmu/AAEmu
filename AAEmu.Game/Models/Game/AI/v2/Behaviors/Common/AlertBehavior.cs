using System;

using AAEmu.Game.Models.Game.Models;
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
            // npc.Events.OnAlert(this, new OnAlertArgs { Npc = npc, Target = npc.CurrentTarget});
            npc.Events.InAlert(this, new InAlertArgs { Npc = npc });
        }

        _pipeName = "phase_dragon_ground";
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        UpdateTarget();
        if ((DateTime.UtcNow > _nextAlertCheckTime) && (Ai.Owner.SkillTask == null))
            Ai.GoToIdle(); // TODO: This should go back to whatever was the last one, but Idle will have to do for now
    }

    public override void Exit()
    {
        _enter = false;
    }
}
