using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ZoneConflictTests
{
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
}
