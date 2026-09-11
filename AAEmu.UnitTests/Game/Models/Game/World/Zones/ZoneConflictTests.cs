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
}
