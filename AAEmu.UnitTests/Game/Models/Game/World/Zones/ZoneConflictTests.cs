using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ZoneConflictTests
{
    [Test]
    public async Task RuntimeState_RestoresParticipationAcrossRestart()
    {
        ConflictZoneRuntimeState saved = default;
        var beforeRestart = new ZoneConflict(
            new ZoneGroup { Id = 14 },
            persist: state => saved = state)
        {
            ZoneGroupId = 14
        };
        for (var level = 0; level < 5; level++)
            beforeRestart.NumKills[level] = 10;
        beforeRestart.AddZoneKill(7);

        var afterRestart = new ZoneConflict(new ZoneGroup { Id = 14 }) { ZoneGroupId = 14 };
        for (var level = 0; level < 5; level++)
            afterRestart.NumKills[level] = 10;
        afterRestart.RestoreRuntimeState(saved, DateTime.UtcNow);

        await Assert.That(afterRestart.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(afterRestart.KillCount).IsEqualTo(7u);
    }

    [Test]
    public async Task PersistenceFailure_RollsBackParticipationMutation()
    {
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 14 },
            persist: _ => throw new InvalidOperationException("store unavailable"))
        {
            ZoneGroupId = 14
        };
        for (var level = 0; level < 5; level++)
            conflict.NumKills[level] = 1;

        conflict.AddZoneKill(2);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.NextStateTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task RestoreExpiredDeadline_ReconcilesOnceFromPersistedBoundary()
    {
        var notifications = new List<(ZoneConflictType Previous, ZoneConflictType Current)>();
        var persisted = new List<ConflictZoneRuntimeState>();
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 14 },
            (_, previous, current) => notifications.Add((previous, current)),
            state => persisted.Add(state))
        {
            ZoneGroupId = 14,
            ConflictMin = 10,
            WarMin = 90,
            PeaceMin = 70
        };
        var now = DateTime.UtcNow;
        var deadline = now.AddMinutes(-5);

        conflict.RestoreRuntimeState(
            new ConflictZoneRuntimeState(14, ZoneConflictType.Conflict, 0, 0, 0, deadline),
            now);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);
        await Assert.That(conflict.NextStateTime).IsEqualTo(deadline.AddMinutes(90));
        await Assert.That(notifications).IsEmpty();
        await Assert.That(persisted).Count().IsEqualTo(1);
        await Assert.That(persisted[0].State).IsEqualTo(ZoneConflictType.War);
    }

    [Test]
    public async Task RestoreWeeksOldRepeatingCycle_SkipsWholeCyclesWithoutPastDeadline()
    {
        var conflict = new ZoneConflict(new ZoneGroup { Id = 30 })
        {
            ZoneGroupId = 30,
            ConflictMin = 5,
            WarMin = 80,
            PeaceMin = 0
        };
        var now = DateTime.UtcNow;
        var oldDeadline = now.AddDays(-21);

        conflict.RestoreRuntimeState(
            new ConflictZoneRuntimeState(30, ZoneConflictType.Conflict, 0, 0, 0, oldDeadline),
            now);

        await Assert.That(conflict.NextStateTime).IsGreaterThan(now);
        await Assert.That(conflict.NextStateTime).IsLessThanOrEqualTo(now.AddMinutes(80));
    }

    [Test]
    public async Task TimerPersistenceFailure_PreservesCommittedPhaseAndSchedulesRetry()
    {
        var fail = false;
        var scheduled = new List<DateTime>();
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 14 },
            persist: _ =>
            {
                if (fail)
                    throw new InvalidOperationException("store unavailable");
            },
            scheduleOverride: due => scheduled.Add(due))
        {
            ZoneGroupId = 14,
            ConflictMin = 10,
            WarMin = 90,
            PeaceMin = 70
        };
        var deadline = DateTime.UtcNow.AddSeconds(-1);
        conflict.RestoreRuntimeState(
            new ConflictZoneRuntimeState(14, ZoneConflictType.Conflict, 0, 0, 0, deadline),
            deadline.AddSeconds(-1));
        scheduled.Clear();
        fail = true;

        conflict.CheckTimer();

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(conflict.NextStateTime).IsEqualTo(deadline);
        await Assert.That(scheduled).Count().IsEqualTo(1);
        await Assert.That(scheduled[0]).IsGreaterThan(DateTime.UtcNow);
    }

    [Test]
    public async Task ScheduledTransition_AppliesWithoutPersistingOrRetrying()
    {
        var notifications = new List<(ZoneConflictType Previous, ZoneConflictType Current)>();
        var scheduled = new List<DateTime>();
        var saveAttempts = 0;
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 20 },
            (_, previous, current) => notifications.Add((previous, current)),
            _ => saveAttempts++,
            due => scheduled.Add(due))
        {
            ZoneGroupId = 20
        };
        var mondayNoon = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local);

        conflict.BindSchedule([new ConflictZoneScheduleEntry(2, 1200, ZoneConflictType.War)], mondayNoon);

        // The row of a schedule-driven zone is never read back, so the transition neither waits on the
        // store nor gets retried when the store is unavailable.
        await Assert.That(saveAttempts).IsEqualTo(0);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);
        // The weekly entry is already due at mondayNoon, so the next change is the following week's.
        await Assert.That(conflict.NextStateTime).IsEqualTo(mondayNoon.AddDays(7).ToUniversalTime());
        await Assert.That(notifications).IsEquivalentTo(new[]
        {
            (Previous: ZoneConflictType.Tension, Current: ZoneConflictType.War)
        });
        await Assert.That(scheduled).IsEquivalentTo(new[] { conflict.NextStateTime });
    }

    [Test]
    public async Task ThresholdEscalation_PreservesConflictDeadlineAndAdvancesToWar()
    {
        var conflict = SteppedZone();
        conflict.BindNoKillDecayMetadata(new ConflictZoneNoKillDecayMetadata(
            conflict.ZoneGroupId,
            Enumerable.Repeat(10, ConflictZoneNoKillDecayMetadata.TroubleStateCount)));

        var before = DateTime.UtcNow;
        conflict.AddZoneKill(6);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(conflict.NextStateTime).IsGreaterThanOrEqualTo(before.AddMinutes(10));
        conflict.ForceNextState();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);
        await Assert.That(conflict.NextStateTime).IsGreaterThanOrEqualTo(before.AddMinutes(90));
    }

    [Test]
    public async Task DailyWarWindow_DrivesStateAndIgnoresParticipation()
    {
        var conflict = SteppedZone();
        conflict.ConflictMin = 720;
        conflict.WarMin = 720;
        conflict.PeaceMin = 0;
        var atWarStart = new DateTime(2026, 9, 22, 7, 0, 0, DateTimeKind.Local);

        conflict.BindDailyWarWindows([new ConflictZoneDailyWarStart(7, 0)], atWarStart);
        conflict.AddZoneKill(100);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
    }

    [Test]
    public async Task InvalidDailyWarWindow_DoesNotDisableParticipation()
    {
        var conflict = SteppedZone();
        var now = new DateTime(2026, 9, 22, 7, 0, 0, DateTimeKind.Local);

        var bound = conflict.BindDailyWarWindows([new ConflictZoneDailyWarStart(7, 0)], now);
        conflict.AddZoneKill(2);

        await Assert.That(bound).IsFalse();
        await Assert.That(conflict.IsScheduleDriven).IsFalse();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Danger);
        await Assert.That(conflict.KillCount).IsEqualTo(2u);
    }

    [Test]
    public async Task SetState_NotifiesAfterAuthoritativeStateChanges()
    {
        var notifications = new List<(ushort ZoneGroupId, ZoneConflictType Previous, ZoneConflictType Current)>();
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 20 },
            (zoneGroupId, previous, current) => notifications.Add((zoneGroupId, previous, current)))
        {
            ZoneGroupId = 20
        };

        conflict.SetState(ZoneConflictType.Danger);
        conflict.SetState(ZoneConflictType.Danger);

        await Assert.That(notifications).IsEquivalentTo(new[]
        {
            (ZoneGroupId: (ushort)20, Previous: ZoneConflictType.Tension, Current: ZoneConflictType.Danger)
        });
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Danger);
    }

    [Test]
    public async Task AddZoneKill_NotifiesForKillDrivenStateChange()
    {
        var notifications = new List<(ZoneConflictType Previous, ZoneConflictType Current)>();
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 20 },
            (_, previous, current) => notifications.Add((previous, current)))
        {
            ZoneGroupId = 20
        };
        conflict.NumKills[0] = 1;
        conflict.NumKills[1] = 100;
        conflict.NumKills[2] = 100;
        conflict.NumKills[3] = 100;
        conflict.NumKills[4] = 100;

        conflict.AddZoneKill(2);

        await Assert.That(notifications).IsEquivalentTo(new[]
        {
            (Previous: ZoneConflictType.Tension, Current: ZoneConflictType.Danger)
        });
    }

    [Test]
    public async Task ScheduledTransition_NotifiesOnceForAnActualStateChange()
    {
        var notifications = new List<(ZoneConflictType Previous, ZoneConflictType Current)>();
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 20 },
            (_, previous, current) => notifications.Add((previous, current)))
        {
            ZoneGroupId = 20
        };
        var mondayNoon = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local);

        conflict.BindSchedule([new ConflictZoneScheduleEntry(2, 1200, ZoneConflictType.War)], mondayNoon);
        conflict.ApplyScheduledState(mondayNoon.AddMinutes(1));

        await Assert.That(notifications).IsEquivalentTo(new[]
        {
            (Previous: ZoneConflictType.Tension, Current: ZoneConflictType.War)
        });
    }

    [Test]
    public async Task ScheduleDrivenTension_IgnoresParticipationCounters()
    {
        var conflict = new ZoneConflict(new ZoneGroup { Id = 20 }) { ZoneGroupId = 20 };
        conflict.NumKills[0] = 1;
        conflict.NumNpcKills[0] = 1;
        conflict.NumQuestCompletions[0] = 1;
        var mondayNoon = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Local);
        conflict.BindSchedule([new ConflictZoneScheduleEntry(2, 1200, ZoneConflictType.Tension)], mondayNoon);

        conflict.AddZoneKill(2);
        conflict.AddNpcKill(2);
        conflict.AddQuestCompletion(2);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.NpcKillCount).IsEqualTo(0u);
        await Assert.That(conflict.QuestCompletionCount).IsEqualTo(0u);
    }

    [Test]
    public async Task NpcOnlyParticipation_ReturnsFromPeaceToTension()
    {
        var conflict = new ZoneConflict(new ZoneGroup { Id = 20 })
        {
            ZoneGroupId = 20,
            PeaceMin = 1
        };
        conflict.NumNpcKills[0] = 1;
        conflict.SetState(ZoneConflictType.Peace);

        conflict.ForceNextState();

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
    }

    /// <summary>A participation zone with one distinct threshold per trouble level.</summary>
    private static ZoneConflict SteppedZone(List<(ZoneConflictType Previous, ZoneConflictType Current)> notifications = null)
    {
        var conflict = new ZoneConflict(
            new ZoneGroup { Id = 14 },
            (_, previous, current) => notifications?.Add((previous, current)))
        {
            ZoneGroupId = 14,
            ConflictMin = 10,
            WarMin = 90,
            PeaceMin = 70
        };
        for (var level = 0; level < 5; level++)
        {
            conflict.NumKills[level] = level + 1;
            conflict.NumNpcKills[level] = level + 1;
            conflict.NumQuestCompletions[level] = level + 1;
        }

        return conflict;
    }

    [Test]
    public async Task AddZoneKill_WalksEveryTroubleStateIntoConflictAtTheThresholds()
    {
        var notifications = new List<(ZoneConflictType Previous, ZoneConflictType Current)>();
        var conflict = SteppedZone(notifications);

        // Thresholds 1..5, strictly greater-than: kill n+1 leaves level n.
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Danger);
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Dispute);
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Unrest);
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Crisis);
        await Assert.That(conflict.KillCount).IsEqualTo(5u);
        await Assert.That(conflict.NextStateTime).IsEqualTo(DateTime.MinValue);

        var before = DateTime.UtcNow;
        conflict.AddZoneKill();

        // Conflict closes the round: counters reset and the conflict_min timer is armed.
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.NextStateTime).IsGreaterThanOrEqualTo(before.AddMinutes(10));
        await Assert.That(conflict.NextStateTime).IsLessThanOrEqualTo(DateTime.UtcNow.AddMinutes(10));
        await Assert.That(notifications).IsEquivalentTo(new[]
        {
            (Previous: ZoneConflictType.Tension, Current: ZoneConflictType.Danger),
            (Previous: ZoneConflictType.Danger, Current: ZoneConflictType.Dispute),
            (Previous: ZoneConflictType.Dispute, Current: ZoneConflictType.Unrest),
            (Previous: ZoneConflictType.Unrest, Current: ZoneConflictType.Crisis),
            (Previous: ZoneConflictType.Crisis, Current: ZoneConflictType.Conflict)
        });
    }

    [Test]
    public async Task AddZoneKill_IsIgnoredInTheTimedStates()
    {
        var conflict = SteppedZone();
        conflict.SetState(ZoneConflictType.Conflict);

        conflict.AddZoneKill(50);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);

        conflict.SetState(ZoneConflictType.War);
        conflict.AddZoneKill(50);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);

        conflict.SetState(ZoneConflictType.Peace);
        conflict.AddZoneKill(50);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Peace);
    }

    [Test]
    public async Task AddNpcKillAndAddQuestCompletion_WalkTheSameLadder()
    {
        var conflict = SteppedZone();

        conflict.AddNpcKill(2);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Danger);

        // Counters do not add up: two more quests only reach stage two on their own ladder.
        conflict.AddQuestCompletion(2);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Danger);

        conflict.AddQuestCompletion(4);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(conflict.NpcKillCount).IsEqualTo(0u);
        await Assert.That(conflict.QuestCompletionCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AddZoneKill_ShippedZone14ReachesConflictAtTheFiftyFirstKill()
    {
        // conflict_zones row 14 e_steppe_belt: num_kills_0..4 = 50, conflict_min 10.
        var conflict = new ZoneConflict(new ZoneGroup { Id = 14 }) { ZoneGroupId = 14, ConflictMin = 10 };
        for (var level = 0; level < 5; level++)
            conflict.NumKills[level] = 50;

        conflict.AddZoneKill(50);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.KillCount).IsEqualTo(50u);

        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
    }

    [Test]
    public async Task AddZoneKill_DoesNothingOnAZoneWithoutThresholds()
    {
        // conflict_zones row 57 o_ruins_of_gold: every num_kills_N is 0.
        var conflict = new ZoneConflict(new ZoneGroup { Id = 57 }) { ZoneGroupId = 57, ConflictMin = 10 };

        conflict.AddZoneKill(10_000);

        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.KillCount).IsEqualTo(0u);
    }

    [Test]
    public async Task ForceNextState_WalksConflictWarPeaceThenRestartsTheCount()
    {
        var conflict = SteppedZone();
        conflict.AddZoneKill(6);
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Conflict);

        conflict.ForceNextState();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.War);
        conflict.ForceNextState();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Peace);
        conflict.ForceNextState();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.NextStateTime).IsEqualTo(DateTime.MinValue);

        // The next round counts from zero again.
        conflict.AddZoneKill();
        await Assert.That(conflict.CurrentZoneState).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(conflict.KillCount).IsEqualTo(1u);
    }
}
