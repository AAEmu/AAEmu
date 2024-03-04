using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.v2.Params.Almighty;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlmightyAttackBehavior : BaseCombatBehavior
{
    private AlmightyNpcAiParams _aiParams;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        _skillQueue = new Queue<AiSkill>();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;

        _combatStartTime = DateTime.UtcNow;

        if (Ai.Owner is { IsInBattle: false } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc });
        }
        Ai.Param = Ai.Owner.Template.AiParams;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (Ai.Param is not AlmightyNpcAiParams aiParams)
            return;

        _aiParams = aiParams;

        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.GoToReturn();
            return;
        }

        if (CanStrafe && !IsUsingSkill)
            MoveInRange(Ai.Owner.CurrentTarget, delta);

        CheckPipeName();

        if (!CanUseSkill)
            return;

        _strafeDuringDelay = false;

        #region Pick a skill

        var delay = 150;
        // Will delay for 150 Milliseconds to eliminate the hanging of the skill
        if (!Ai.Owner.CheckInterval(delay))
        {
            Logger.Trace($"Skill: CooldownTime [{delay}]!");
        }
        else
        {
            if (_skillQueue.Count == 0)
            {
                if (!RefreshSkillQueue(_aiParams.AiSkillLists, _aiParams))
                    return;
            }

            var selectedSkill = _skillQueue.Dequeue();
            if (selectedSkill == null)
                return;
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(selectedSkill.SkillId);
            if (skillTemplate == null)
                return;

            UseSkill(new Skill(skillTemplate), Ai.Owner.CurrentTarget, selectedSkill.Delay);

            _strafeDuringDelay = selectedSkill.Strafe;
        }

        #endregion
    }

    public override void Exit()
    {
        _enter = false;
    }
}
