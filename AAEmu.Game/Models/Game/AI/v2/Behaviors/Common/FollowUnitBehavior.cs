using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class FollowUnitBehavior : BaseCombatBehavior
{
    private bool _enter;
    
    public override void Enter()
    {
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return;

        if (!UpdateTarget())
            Ai.Owner.SetTarget(Ai.Owner);

        if (CheckAggression())
            return;

        if (CheckAlert())
            return;

        if (Ai.AiFollowUnitObj == null || Ai.AiFollowUnitObj.Hp <= 0 || Ai.AiFollowUnitObj.IsDead)
        {
            Ai.AiFollowUnitObj = null;
            Ai.GoToIdle();
            return;
        }

        var targetDistance = Ai.Owner.GetDistanceTo(Ai.AiFollowUnitObj, true);

        var followSpeedMultiplier = (float)Math.Min(Math.Sqrt(targetDistance), targetDistance / 2.0);
        
        Ai.Owner.MoveTowards(Ai.AiFollowUnitObj.Transform.World.Position, followSpeedMultiplier * Ai.Owner.BaseMoveSpeed * (delta.Milliseconds / 1000.0f), 4);
        Ai.IdlePosition = Ai.Owner.Transform.World.Position;
    }

    public override void Exit()
    {
        _enter = false;
    }
}
