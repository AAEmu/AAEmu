﻿using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RoamingBehavior : Behavior
{
    private Vector3 _targetRoamPosition = Vector3.Zero;
    private DateTime _nextRoaming;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, BaseUnitType.Npc, ModelPostureType.ActorModelState, 2), false); // fixed animated
        UpdateRoaming();
    }

    public override void Tick(TimeSpan delta)
    {
        if (Ai.Owner.Template.Aggression)
        {
            var nearbyUnits = WorldManager.GetAround<Unit>(Ai.Owner, CheckSightRangeScale(10f));

            foreach (var unit in nearbyUnits)
            {
                if (Ai.Owner.Template.Aggression)
                {
                    //Need to check for stealth detection here..
                    if (Ai.Owner.Template.SightFovScale >= 2.0f || MathUtil.IsFront(Ai.Owner, unit))
                    {
                        if (Ai.Owner.CanAttack(unit))
                        {
                            OnEnemySeen(unit);
                            break;
                        }
                    }
                    else
                    {
                        var rangeOfUnit = MathUtil.CalculateDistance(Ai.Owner, unit, true);
                        if (rangeOfUnit < 3 * Ai.Owner.Template.SightRangeScale)
                        {
                            if (Ai.Owner.CanAttack(unit))
                            {
                                OnEnemySeen(unit);
                                break;
                            }
                        }
                    }
                }
            }
        }

        if (_targetRoamPosition.Equals(Vector3.Zero) && DateTime.UtcNow > _nextRoaming)
            UpdateRoaming();

        if (_targetRoamPosition.Equals(Vector3.Zero))
            return;

        Ai.Owner.MoveTowards(_targetRoamPosition, 1.8f * (delta.Milliseconds / 1000.0f), 5);
        var dist = MathUtil.CalculateDistance(Ai.Owner.Transform.World.Position, _targetRoamPosition, true);
        if (dist < 1.0f)
        {
            Ai.Owner.StopMovement();
            _targetRoamPosition = Vector3.Zero;
            _nextRoaming = DateTime.UtcNow.AddSeconds(Rand.Next(3, 6)); // Rand 3-6 would look nice ?
        }
    }

    public override void Exit()
    {
    }

    private void UpdateRoaming()
    {
        // TODO : Group member handling
        using (var transform = AIUtils.CalcNextRoamingPosition(Ai))
        {
            _targetRoamPosition = transform.Local.Position;
        }
    }
}
