using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RoamingBehavior : BaseCombatBehavior
{
    private Vector3 _targetRoamPosition = Vector3.Zero;
    private DateTime _nextRoaming;
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        //Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, BaseUnitType.Npc, ModelPostureType.ActorModelState, Ai.Owner.Template.AnimActionId, false), false); // fixed animated
        //UpdateRoaming();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (!CheckAggression())
            CheckAlert();

        if (_targetRoamPosition.Equals(Vector3.Zero) && DateTime.UtcNow > _nextRoaming)
        {
            UpdateRoaming();
            Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, false), false);
        }

        if (_targetRoamPosition.Equals(Vector3.Zero))
            return;

        var moveSpeed = Ai.GetRealMovementSpeed(Ai.Owner.BaseMoveSpeed);
        var moveFlags = Ai.GetRealMovementFlags(moveSpeed);
        moveSpeed *= (delta.Milliseconds / 1000.0);
        Ai.Owner.MoveTowards(_targetRoamPosition, (float)moveSpeed, moveFlags);

        var dist = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, _targetRoamPosition);
        if (dist < 1.0f)
        {
            Ai.Owner.StopMovement();
            _targetRoamPosition = Vector3.Zero;
            _nextRoaming = DateTime.UtcNow.AddSeconds(Rand.Next(3, 6)); // Rand 3-6 would look nice ?
            Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, Ai.Owner.AnimActionId, true), false);
        }
    }

    public override void Exit()
    {
        _enter = false;
    }

    private void UpdateRoaming()
    {
        // TODO : Group member handling
        _targetRoamPosition = AIUtils.CalcNextRoamingPosition(Ai);
    }
}
