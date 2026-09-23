using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Residents;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Resident point and charge settlement against the in-memory store: rows settle once per
/// character and zone, an unresolved type2 is refused, hunting charge is kept apart from local
/// charge, and a settlement does not move tribute doodads.
/// </summary>
[NotInParallel]
public sealed class ResidentManagerTests
{
    private const uint CharacterId = 42;
    private const short ZoneGroup = 102;
    private const ushort ZoneGroup16 = 102;

    private ResidentManager _manager;
    private InMemoryResidentStateStore _store;

    [Before(Test)]
    public void Setup()
    {
        _store = new InMemoryResidentStateStore();
        _manager = ResidentManager.Instance;
        _manager.ResetForTest();
        _manager.UseStore(_store);
    }

    [After(Test)]
    public void Teardown()
    {
        _manager.ResetForTest();
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
    public async Task Charge_SettlesLocalAndHuntingApartAndRefusesAnUnresolvedType()
    {
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 5000, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Settled);
        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 50, moneyAmount2: 9))
            .IsEqualTo(ResidentSettleStatus.Settled);

        await Assert.That(_manager.AddCharge(CharacterId, ZoneGroup, type2: 7, moneyAmount: 10, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Refused);

        var rows = _store.LoadAll();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].Charge).IsEqualTo(5050ul);
        await Assert.That(rows[0].HuntingCharge).IsEqualTo(9ul);
        await Assert.That(_manager.GetZoneChargeSum(ZoneGroup16)).IsEqualTo(5050ul);
        await Assert.That(_manager.GetZoneHuntingChargeSum(ZoneGroup16)).IsEqualTo(9ul);
    }

    [Test]
    public async Task ServicePoints_DoNotMoveTheTributeDoodad()
    {
        await Assert.That(_manager.AddServicePoint(CharacterId, ZoneGroup, 60)).IsEqualTo(ResidentSettleStatus.Settled);
        await Assert.That(_manager.AddServicePoint(CharacterId, ZoneGroup, 40)).IsEqualTo(ResidentSettleStatus.Settled);

        await Assert.That(_manager.GetServicePoint(CharacterId, ZoneGroup16)).IsEqualTo(100u);
    }

    [Test]
    public async Task AnyZoneGroup_SettlesTheRow()
    {
        var status = _manager.AddServicePoint(CharacterId, 4000, 10);
        await Assert.That(status).IsEqualTo(ResidentSettleStatus.Settled);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);

        // An invalid zone group refuses outright: nothing written, no development run.
        await Assert.That(_manager.AddServicePoint(CharacterId, 0, 10)).IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_manager.AddServicePoint(CharacterId, -1, 10)).IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_manager.AddCharge(CharacterId, -1, type2: 0, moneyAmount: 5, moneyAmount2: 0))
            .IsEqualTo(ResidentSettleStatus.Refused);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);
    }

    [Test]
    public async Task BoardAndCharacterState_RoundTripThroughTheStore()
    {
        _manager.AddServicePoint(CharacterId, ZoneGroup, 60);
        _manager.AddServicePoint(CharacterId, ZoneGroup, 40);
        _manager.AddCharge(CharacterId, ZoneGroup, type2: 0, moneyAmount: 250, moneyAmount2: 0);

        var pointBefore = _manager.GetServicePoint(CharacterId, ZoneGroup16);
        var chargeBefore = _manager.GetCharge(CharacterId, ZoneGroup16);

        _manager.LoadFromStore();

        await Assert.That(_manager.GetServicePoint(CharacterId, ZoneGroup16)).IsEqualTo(pointBefore);
        await Assert.That(_manager.GetCharge(CharacterId, ZoneGroup16)).IsEqualTo(chargeBefore);
        await Assert.That(_store.LoadAll().Count).IsEqualTo(1);
    }
}
