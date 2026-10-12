using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

public class HousingResidenceWireRulesTests
{
    [Test]
    public async Task WireOwnerIdForList_ExpeditionResidence_UsesGuildMarker()
    {
        await Assert.That(HousingResidenceWireRules.WireOwnerIdForList(8, isExpeditionResidence: true))
            .IsEqualTo(HousingWireMarkers.ExpeditionResidenceOwnerId);
    }

    [Test]
    public async Task WireOwnerIdForList_PersonalHouse_KeepsCharacterOwner()
    {
        await Assert.That(HousingResidenceWireRules.WireOwnerIdForList(8, isExpeditionResidence: false))
            .IsEqualTo(8u);
    }

    [Test]
    public async Task WireOwnerIdForState_AlwaysUsesCharacterOwner()
    {
        await Assert.That(HousingResidenceWireRules.WireOwnerIdForState(8)).IsEqualTo(8u);
        await Assert.That(HousingResidenceWireRules.WireOwnerIdForState(1008)).IsEqualTo(1008u);
    }

    [Test]
    public async Task WireOwnerName_ExpeditionResidence_UsesGuildName()
    {
        await Assert.That(HousingResidenceWireRules.WireOwnerName("Tester", "AISER", true))
            .IsEqualTo("AISER");
    }

    [Test]
    public async Task WireOwnerName_PersonalHouse_KeepsCharacterName()
    {
        await Assert.That(HousingResidenceWireRules.WireOwnerName("Tester", "AISER", false))
            .IsEqualTo("Tester");
    }
}
