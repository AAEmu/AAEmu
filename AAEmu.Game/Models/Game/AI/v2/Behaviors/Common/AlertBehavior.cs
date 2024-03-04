using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlertBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        var buffId = 6586u;
        Ai.Owner.Buffs.RemoveBuff(6582);
        Ai.Owner.Buffs.AddBuff(new Buff(Ai.Owner, Ai.Owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));

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

        UpdateTarget();
    }

    public override void Exit()
    {
        _enter = false;
    }
}
