using AAEmu.Game.Models.Game.Events;

namespace AAEmu.UnitTests.Game.Models.Game.Events;

public class CrimsonRiftTests
{
    [Test]
    public async Task TriggerHour_Is13()
    {
        await Assert.That(CrimsonRift.TriggerHour).IsEqualTo(13f);
    }

    [Test]
    public async Task ForceEndHour_Is16()
    {
        await Assert.That(CrimsonRift.ForceEndHour).IsEqualTo(16f);
    }

    [Test]
    public async Task TriggerHour_PrecedesForceEndHour()
    {
        await Assert.That(CrimsonRift.TriggerHour).IsLessThan(CrimsonRift.ForceEndHour);
    }

    [Test]
    [Arguments(12.99f, false)]
    [Arguments(13.00f, true)]
    [Arguments(15.99f, true)]
    [Arguments(16.00f, false)]
    public async Task IsWithinActiveWindow_MatchesStartAndForceStopWindow(float hour, bool expected)
    {
        await Assert.That(CrimsonRift.IsWithinActiveWindow(hour)).IsEqualTo(expected);
    }

    [Test]
    public async Task PhaseTemplates_MatchQuestMonsterGroups()
    {
        await Assert.That(CrimsonRift.Phase1Mobs).IsEquivalentTo([8826u, 8834u]);
        await Assert.That(CrimsonRift.Phase2Mobs).IsEquivalentTo([8827u, 8835u]);
        await Assert.That(CrimsonRift.Phase3Mobs).IsEquivalentTo([8836u, 8825u]);
        await Assert.That(CrimsonRift.Phase4Mobs).IsEquivalentTo([8850u]);
    }

    [Test]
    public async Task Phases_HaveNoOverlap()
    {
        var phases = new[]
        {
            CrimsonRift.Phase1Mobs,
            CrimsonRift.Phase2Mobs,
            CrimsonRift.Phase3Mobs,
            CrimsonRift.Phase4Mobs,
        };

        for (var left = 0; left < phases.Length; left++)
        {
            for (var right = left + 1; right < phases.Length; right++)
            {
                await Assert.That(phases[left].Intersect(phases[right])).IsEmpty();
            }
        }
    }

    [Test]
    public async Task SpawnPoints_DeclareExpectedRegions()
    {
        await Assert.That(CrimsonRift.SpawnPointsByRegion.ContainsKey("Ynystere")).IsTrue();
        await Assert.That(CrimsonRift.SpawnPointsByRegion.ContainsKey("Cinderstone")).IsTrue();
        await Assert.That(CrimsonRift.SpawnPointsByRegion["Ynystere"]).IsNotEmpty();
    }

    [Test]
    public async Task TowerDefIds_MatchClientDb()
    {
        await Assert.That(CrimsonRift.TowerDefIdsByRegion["Ynystere"]).IsEqualTo(5u);
        await Assert.That(CrimsonRift.TowerDefIdsByRegion["Cinderstone"]).IsEqualTo(3u);
        await Assert.That(CrimsonRift.TowerDefIdsByRegion["Auroria"]).IsEqualTo(6u);
    }

    [Test]
    public async Task TowerDefIds_CoverAllSpawnRegions()
    {
        foreach (var region in CrimsonRift.SpawnPointsByRegion.Keys)
        {
            await Assert.That(CrimsonRift.TowerDefIdsByRegion.ContainsKey(region)).IsTrue();
        }
    }

    [Test]
    [Arguments(12.5f, 13.0f, 13f, true)]
    [Arguments(12.99f, 13.01f, 13f, true)]
    [Arguments(15.9f, 16.1f, 16f, true)]
    [Arguments(10f, 12f, 13f, false)]
    [Arguments(14f, 15f, 13f, false)]
    [Arguments(13f, 13.5f, 13f, false)]
    [Arguments(23.5f, 0.5f, 0f, true)]
    [Arguments(23f, 14f, 13f, true)]
    [Arguments(23.5f, 0.5f, 13f, false)]
    [Arguments(20f, 2f, 15f, false)]
    public async Task CrossedHour_DetectsTransitions(float previousHour, float currentHour, float targetHour, bool expected)
    {
        await Assert.That(CrimsonRift.CrossedHour(previousHour, currentHour, targetHour)).IsEqualTo(expected);
    }

    [Test]
    public async Task OnNext_StartsEvent_WhenClockCrossesTriggerHour()
    {
        var rift = new TestCrimsonRift();

        rift.OnNext(12.99f);
        rift.OnNext(13.01f);

        await Assert.That(rift.ScheduleStartCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnNext_StartsEvent_WhenFirstSampleIsInsideActiveWindow()
    {
        var rift = new TestCrimsonRift();

        rift.OnNext(13.5f);

        await Assert.That(rift.ScheduleStartCount).IsEqualTo(1);
    }

    [Test]
    public async Task ObserverNoOps_DoNotThrow()
    {
        var rift = new CrimsonRift();

        rift.OnError(new InvalidOperationException("boom"));
        rift.OnCompleted();

        await Assert.That(rift).IsNotNull();
    }

    private sealed class TestCrimsonRift : CrimsonRift
    {
        public int ScheduleStartCount { get; private set; }

        protected override void StartFromSchedule()
        {
            ScheduleStartCount++;
        }
    }
}
