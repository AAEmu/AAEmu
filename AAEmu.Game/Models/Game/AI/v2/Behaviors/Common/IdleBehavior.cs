using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class IdleBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        // BUFF: Fly
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.SetTarget(null);
        Ai.Owner.SendPacketToPlayers([Ai.Owner.CurrentTarget], new SCAggroTargetChangedPacket(Ai.Owner.ObjId, 0));
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        if (Ai.Owner is { } npc)
        {
            npc.Events.InIdle(this, new InIdleArgs { Owner = npc });
        }
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

        if (CheckAggression())
        {
            Ai.GoToCombat();
        }
        else if (CheckAlert())
        {
            Ai.GoToAlert();
        }
        else if (CheckFollowPath())
        {
            Ai.GoToFollowPath();
        }
        else
        {
            Ai.GoToDefaultBehavior();
        }
    }

    public override void Exit()
    {
        _enter = false;
    }
}
