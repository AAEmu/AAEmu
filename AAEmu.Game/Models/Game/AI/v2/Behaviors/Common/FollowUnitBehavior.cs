using System;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class FollowUnitBehavior : BaseCombatBehavior
{
    private bool _enter;
    
    public override void Enter()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
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

        var followSpeedMultiplier = (float)Math.Min(5.0, targetDistance / 1.5);
        
        var moveSpeed = Ai.GetRealMovementSpeed() * followSpeedMultiplier;
        var moveFlags = Ai.GetRealMovementFlags(moveSpeed);
        moveSpeed *= (delta.Milliseconds / 1000.0);
        Ai.Owner.MoveTowards(Ai.AiFollowUnitObj.Transform.World.Position, (float)moveSpeed, moveFlags);
        Ai.IdlePosition = Ai.Owner.Transform.World.Position;
    }

    public override void Exit()
    {
        _enter = false;
    }
}
