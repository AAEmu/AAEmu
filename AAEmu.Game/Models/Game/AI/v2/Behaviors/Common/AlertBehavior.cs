using System;
using System.Numerics;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlertBehavior : BaseCombatBehavior
{
    private bool _enter;
    private Vector3 _oldRotation;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();

        CheckPipeName();
        Ai.Owner.StopMovement();
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, BaseUnitType.Npc, ModelPostureType.ActorModelState, 2), false);
        if (Ai.Owner.CurrentTarget != null)
        {
            _oldRotation = Ai.Owner.Transform.Local.Rotation;
            Ai.Owner.LookTowards(Ai.Owner.CurrentTarget.Transform.World.Position);
        }

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

        // UpdateTarget();
        if (Ai.Owner.CurrentTarget != null)
        {
            Ai.Owner.LookTowards(Ai.Owner.CurrentTarget.Transform.World.Position);
        }
        if ((DateTime.UtcNow > Ai._alertEndTime) && (Ai.Owner.SkillTask == null))
        {
            // Ai.Owner.SetTarget(null);
            Ai.Owner.Transform.Local.SetRotation(_oldRotation.X,_oldRotation.Y, _oldRotation.Z);
            Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, BaseUnitType.Npc, ModelPostureType.ActorModelState), false);
            Ai.GoToIdle(); // TODO: This should go back to whatever was the last one, but Idle will have to do for now
            return;
        }

        CheckAggression();
    }

    public override void Exit()
    {
        Ai.Owner.SetTarget(null);
        _enter = false;
    }
}
