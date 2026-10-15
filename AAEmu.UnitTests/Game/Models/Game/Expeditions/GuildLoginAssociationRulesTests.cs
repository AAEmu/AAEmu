using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class GuildLoginAssociationRulesTests
{
    [Test]
    public async Task Resolve_CharacterRowWinsWhenNonZero()
    {
        var decision = GuildLoginAssociationRules.Resolve(77, 99);

        await Assert.That(decision.ExpeditionId).IsEqualTo(77u);
        await Assert.That(decision.HealCharacterRow).IsFalse();
    }

    [Test]
    public async Task Resolve_HealsFromRosterWhenCharacterRowIsZero()
    {
        var decision = GuildLoginAssociationRules.Resolve(0, 77);

        await Assert.That(decision.ExpeditionId).IsEqualTo(77u);
        await Assert.That(decision.HealCharacterRow).IsTrue();
    }

    [Test]
    public async Task Resolve_StaysGuildlessWhenBothSidesEmpty()
    {
        var noRoster = GuildLoginAssociationRules.Resolve(0, null);
        var zeroRoster = GuildLoginAssociationRules.Resolve(0, 0);

        await Assert.That(noRoster.ExpeditionId).IsEqualTo(0u);
        await Assert.That(noRoster.HealCharacterRow).IsFalse();
        await Assert.That(zeroRoster.ExpeditionId).IsEqualTo(0u);
        await Assert.That(zeroRoster.HealCharacterRow).IsFalse();
    }
}
