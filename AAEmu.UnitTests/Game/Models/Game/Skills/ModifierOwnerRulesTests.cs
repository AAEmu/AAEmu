using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The owner_type decision both modifier loaders share. Filing an Item row under its owner_id as if it were a
/// buff id is what let an item's modifier be granted by an unrelated buff that happened to share the id.
/// </summary>
public class ModifierOwnerRulesTests
{
    [Test]
    public async Task TheFourShippedOwnerTypesAreClassified()
    {
        await Assert.That(ModifierOwnerRules.Classify("Buff")).IsEqualTo(ModifierOwner.Buff);
        await Assert.That(ModifierOwnerRules.Classify("Item")).IsEqualTo(ModifierOwner.Item);
        await Assert.That(ModifierOwnerRules.Classify("ExpeditionBuffGrade")).IsEqualTo(ModifierOwner.ExpeditionBuffGrade);
        await Assert.That(ModifierOwnerRules.Classify("CombatResource")).IsEqualTo(ModifierOwner.CombatResource);
    }

    [Test]
    public async Task AnyOtherOwnerIsUnknownRatherThanABuff()
    {
        // A future client's owner_type must not be filed under a buff id: that is the bug this rule closes.
        await Assert.That(ModifierOwnerRules.Classify("")).IsEqualTo(ModifierOwner.Unknown);
        await Assert.That(ModifierOwnerRules.Classify("buff")).IsEqualTo(ModifierOwner.Unknown);
        await Assert.That(ModifierOwnerRules.Classify("Doodad")).IsEqualTo(ModifierOwner.Unknown);
    }
}
