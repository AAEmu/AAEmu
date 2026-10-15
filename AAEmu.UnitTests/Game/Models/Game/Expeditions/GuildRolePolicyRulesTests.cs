using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class GuildRolePolicyRulesTests
{
    [Test]
    public async Task NeedsDefaultPolicies_WhenEmpty_GatesLoadFallback()
    {
        // ExpeditionManager.Load seeds GetDefaultPolicies when the table has no rows.
        await Assert.That(GuildRolePolicyRules.NeedsDefaultPolicies(0)).IsTrue();
        await Assert.That(GuildRolePolicyRules.NeedsDefaultPolicies(-1)).IsTrue();
        await Assert.That(GuildRolePolicyRules.NeedsDefaultPolicies(5)).IsFalse();
    }
}
