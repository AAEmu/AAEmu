using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunPortalListRulesTests
{
    private static IndunPortalPoint Portal(uint indunZone, uint portalZone)
        => new(indunZone, portalZone, 0f, 0f, 0f);

    [Test]
    public async Task Build_OrdersByInstanceZoneThenPortalZone()
    {
        var rows = IndunPortalListRules.Build([Portal(273, 9), Portal(130, 4), Portal(130, 2)]);

        await Assert.That(rows.Count).IsEqualTo(3);
        await Assert.That(rows[0].IndunZoneKey).IsEqualTo(130u);
        await Assert.That(rows[0].PortalZoneKey).IsEqualTo(2u);
        await Assert.That(rows[1].PortalZoneKey).IsEqualTo(4u);
        await Assert.That(rows[2].IndunZoneKey).IsEqualTo(273u);
    }

    [Test]
    public async Task Build_CollapsesTheSameInstanceAndZonePair()
    {
        // The window shows one entry per pair, so a second portal to the same instance in the same zone is
        // the same row.
        var rows = IndunPortalListRules.Build([Portal(130, 4), Portal(130, 4)]);

        await Assert.That(rows.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Build_KeepsTheSameInstanceInAnotherZone()
    {
        // Two doors to one instance on two continents are two entries: the window can point at both.
        var rows = IndunPortalListRules.Build([Portal(130, 4), Portal(130, 9)]);

        await Assert.That(rows.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Build_DropsPortalsThatLeadNowhere()
    {
        var rows = IndunPortalListRules.Build([Portal(0, 4), Portal(130, 4)]);

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].IndunZoneKey).IsEqualTo(130u);
    }

    [Test]
    public async Task Build_NoPortalsIsAnEmptyList() =>
        await Assert.That(IndunPortalListRules.Build(null).Count).IsEqualTo(0);

    [Test]
    public async Task IsInstancePortalFunc_OnlyTheTwoEnterInstanceFuncs()
    {
        await Assert.That(IndunPortalListRules.IsInstancePortalFunc("DoodadFuncEnterInstance")).IsTrue();
        await Assert.That(IndunPortalListRules.IsInstancePortalFunc("DoodadFuncEnterSysInstance")).IsTrue();
        await Assert.That(IndunPortalListRules.IsInstancePortalFunc("DoodadFuncEnterHouse")).IsFalse();
        await Assert.That(IndunPortalListRules.IsInstancePortalFunc("")).IsFalse();
        await Assert.That(IndunPortalListRules.IsInstancePortalFunc(null)).IsFalse();
    }

    [Test]
    public async Task CanEnter_UsesTheInstancesOwnRequirements()
    {
        // Inside the band, no gear requirement.
        await Assert.That(IndunPortalListRules.CanEnter(50u, 70u, 0u, 55, 1)).IsTrue();
        // The band decides.
        await Assert.That(IndunPortalListRules.CanEnter(56u, 70u, 0u, 55, 9999)).IsFalse();
        await Assert.That(IndunPortalListRules.CanEnter(50u, 54u, 0u, 55, 9999)).IsFalse();
        // A gear requirement the character does not reach is a refusal; reaching it is not.
        await Assert.That(IndunPortalListRules.CanEnter(50u, 70u, 8000u, 55, 7999)).IsFalse();
        await Assert.That(IndunPortalListRules.CanEnter(50u, 70u, 8000u, 55, 8000)).IsTrue();
        // A row with no gear requirement accepts any score.
        await Assert.That(IndunPortalListRules.CanEnter(50u, 70u, 0u, 55, 0)).IsTrue();
    }
}
