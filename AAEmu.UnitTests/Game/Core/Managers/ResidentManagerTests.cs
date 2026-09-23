using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Resident point/charge settlement and the development state machine end to end against the
/// in-memory store: rows settle exactly once per character/zone, unresolved charge fields are
/// refused loudly, contribution crosses content thresholds into doodad/board phases, and both
/// kinds of state round-trip through the store the way a restart reads them back.
/// </summary>
[NotInParallel]
public sealed class ResidentManagerTests
{
    private const uint CharacterId = 42;
    private const short ZoneGroup = 102;
    private const ushort ZoneGroup16 = 102;

    private ResidentManager _manager;
    private InMemoryResidentStateStore _store;
    private readonly List<LocalDevelopmentPlan> _applied = [];

    [Before(Test)]
    public void Setup()
    {
        var content = LocalDevelopmentGameData.Instance;
        content.ResetForTest();
        content.SeedForTest(TwoThresholdDevelopment());

        _store = new InMemoryResidentStateStore();
        _manager = ResidentManager.Instance;
        _manager.ResetForTest();
        _manager.UseStore(_store);
        _applied.Clear();
        _manager.PhaseApplier = (_, plan) => _applied.Add(plan);
    }

    [After(Test)]
    public void Teardown()
    {
        _manager.PhaseApplier = null;
        _manager.ResetForTest();
        LocalDevelopmentGameData.Instance.ResetForTest();
    }

    private static LocalDevelopmentDefinition TwoThresholdDevelopment()
    {
        var definition = new LocalDevelopmentDefinition
        {
            Id = 32,
            ZoneGroupId = ZoneGroup16,
            DoodadAlmightyId = 11590,
            BoardDoodadId = 13600,
            DoodadPhases = [40000, 40001, 40002, 40003],
        };
        definition.BoardRows.Add(new LocalDevelopmentBoardRow(1, 5, 47635, 60));
        definition.BoardRows.Add(new LocalDevelopmentBoardRow(2, 5, 47636, 100));
        return definition;
    }

    [Test]
    public async Task ServicePoints_AggregateIntoASinglePersistedRowPerCharacterAndZone()
    {
        var first = _manager.AddServicePoint(CharacterId, ZoneGroup, 60);
        var second = _manager.AddServicePoint(CharacterId, ZoneGroup, 5);

        await Assert.That(first).IsEqualTo(ResidentSettleStatus.Settled);
        await Assert.That(second).IsEqualTo(ResidentSettleStatus.Settled);

        var rows = _store.LoadAll();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].OwnerId).IsEqualTo(CharacterId);
        await Assert.That(rows[0].ZoneGroupId).IsEqualTo(ZoneGroup16);
        await Assert.That(rows[0].ServicePoint).IsEqualTo(65u);
        await Assert.That(_manager.GetZonePointSum(ZoneGroup16)).IsEqualTo(65u);
        await Assert.That(_manager.GetServicePoint(CharacterId, ZoneGroup16)).IsEqualTo(65u);
    }

    [Test]
    public async Task Charge_SettlesOnceAndRefusesTheUnresolvedFieldsLoudly()
    {
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 5000, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Settled);
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 50, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Settled);

        // type2 and the second moneyAmount have no modelled meaning: non-zero refuses the
        // whole settlement, and nothing lands in the store.
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 7, moneyAmount: 10, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 10, moneyAmount2: 9))
            .IsEqualTo(ResidentSettleStatus.Refused);

        var rows = _store.LoadAll();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Charge).IsEqualTo(5050ul);
        await Assert.That(_manager.GetZoneChargeSum(ZoneGroup16)).IsEqualTo(5050ul);
    }

    [Test]
    public async Task Contribution_CrossesContentThresholdsIntoDoodadAndBoardPhases()
    {
        // Below every threshold: the base phase, no board notice.
        _manager.AddServicePoint(CharacterId, ZoneGroup, 0);
        await Assert.That(_applied[^1].Level).IsEqualTo(0u);
        await Assert.That(_applied[^1].DoodadPhase).IsEqualTo((uint?)40000);
        await Assert.That(_applied[^1].BoardPhase).IsNull();

        // Cross the first threshold (60): level 1 picks doodad_phase_1 and the 60-board phase.
        _manager.AddServicePoint(CharacterId, ZoneGroup, 60);
        await Assert.That(_applied[^1].Level).IsEqualTo(1u);
        await Assert.That(_applied[^1].DoodadPhase).IsEqualTo((uint?)40001);
        await Assert.That(_applied[^1].BoardPhase).IsEqualTo((uint?)47635);

        // Total 100 crosses the second threshold: level 2, board flips to the 100-notice.
        _manager.AddServicePoint(CharacterId, ZoneGroup, 40);
        await Assert.That(_applied[^1].Level).IsEqualTo(2u);
        await Assert.That(_applied[^1].DoodadPhase).IsEqualTo((uint?)40002);
        await Assert.That(_applied[^1].BoardPhase).IsEqualTo((uint?)47636);

        var state = _manager.GetDevelopmentState(ZoneGroup16);
        await Assert.That(state).IsNotNull();
        await Assert.That(state.DevelopmentLevel).IsEqualTo(2u);
        await Assert.That(state.DoodadPhase).IsEqualTo(40002u);
        await Assert.That(state.BoardPhase).IsEqualTo(47636u);

        var stored = _store.LoadDevelopmentStates();
        await Assert.That(stored.Count).IsEqualTo(1);
        await Assert.That(stored[0]).IsEqualTo(state);
    }

    [Test]
    public async Task ZoneGroupWithoutDevelopment_SettlesTheRowAndSkipsThePhaseLoudly()
    {
        // Quest resident acts target zone groups (33/34/43/44) that have no local_developments
        // row: the contribution must still settle, the phase step must be the loud skip.
        var status = _manager.AddServicePoint(CharacterId, 4000, 10);
        await Assert.That(status).IsEqualTo(ResidentSettleStatus.SettledDevelopmentSkipped);
        await Assert.That(_applied).IsEmpty();
        await Assert.That(_manager.GetDevelopmentState(4000)).IsNull();
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);

        // An invalid zone group refuses outright: nothing written, no development run.
        await Assert.That(_manager.AddServicePoint(CharacterId, 0, 10)).IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_manager.AddServicePoint(CharacterId, -1, 10)).IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_manager.AddCharge(CharacterId, -1, type2: 0, moneyAmount: 5, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);
        await Assert.That(_applied).IsEmpty();
    }

    [Test]
    public async Task UndefinedDoodadPhase_SkipsTheAlmightyAndKeepsTheRestOfThePlan()
    {
        var definition = new LocalDevelopmentDefinition
        {
            Id = 34,
            ZoneGroupId = 57,
            DoodadAlmightyId = 11592,
            BoardDoodadId = 13602,
            DoodadPhases = [-1, -1, -1, -1], // content default: nothing defined
        };
        definition.BoardRows.Add(new LocalDevelopmentBoardRow(10, 5, 47643, 60));
        LocalDevelopmentGameData.Instance.SeedForTest(definition);

        var status = _manager.AddServicePoint(CharacterId, 57, 60);

        await Assert.That(status).IsEqualTo(ResidentSettleStatus.Settled);
        var plan = _applied[^1];
        await Assert.That(plan.Level).IsEqualTo(1u);
        await Assert.That(plan.DoodadPhase).IsNull(); // loud skip — never a fallback func group
        await Assert.That(plan.BoardPhase).IsEqualTo((uint?)47643);

        var state = _manager.GetDevelopmentState(57);
        await Assert.That(state.DevelopmentLevel).IsEqualTo(1u);
        await Assert.That(state.DoodadPhase).IsEqualTo(0u); // nothing applied
        await Assert.That(state.BoardPhase).IsEqualTo(47643u);
    }

    [Test]
    public async Task BoardAndCharacterState_RoundTripThroughTheStore()
    {
        _manager.AddServicePoint(CharacterId, ZoneGroup, 60);
        _manager.AddServicePoint(CharacterId, ZoneGroup, 40);
        _manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 250, moneyAmount2: 0);

        var developmentBefore = _manager.GetDevelopmentState(ZoneGroup16);
        var pointBefore = _manager.GetServicePoint(CharacterId, ZoneGroup16);
        var chargeBefore = _manager.GetCharge(CharacterId, ZoneGroup16);

        // Drop every in-memory row and reload from the same store: what a restart reads back.
        _manager.LoadFromStore();

        await Assert.That(_manager.GetDevelopmentState(ZoneGroup16)).IsEqualTo(developmentBefore);
        await Assert.That(_manager.GetServicePoint(CharacterId, ZoneGroup16)).IsEqualTo(pointBefore);
        await Assert.That(_manager.GetCharge(CharacterId, ZoneGroup16)).IsEqualTo(chargeBefore);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);
        await Assert.That(_store.LoadDevelopmentStates().Count).IsEqualTo(1);
    }
}
