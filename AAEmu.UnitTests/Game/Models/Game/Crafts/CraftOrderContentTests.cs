using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// Craft-order catalog keys. Numeric ids are loaded from compact; these names are how the rows
/// are found.
/// </summary>
public class CraftOrderContentTests
{
    [Test]
    public async Task CatalogKeys_AreTheCompactConstNames()
    {
        await Assert.That(CraftOrderContent.SheetItemConstName).IsEqualTo("craft_order");
        await Assert.That(CraftOrderContent.MakeSheetSkillConstName).IsEqualTo("make_craft_order_sheet");
        await Assert.That(CraftOrderContent.RestoreSheetSkillConstName).IsEqualTo("restore_craft_order_sheet");
        await Assert.That(CraftOrderContent.ProcessSkillConstName).IsEqualTo("process_craft_order");
        await Assert.That(CraftOrderContent.InstantSkillConstName).IsEqualTo("process_craft_order_instant");
        await Assert.That(CraftOrderContent.ChargeConfigName).IsEqualTo("craft_order_charge_for_resident");
    }

    [Test]
    public async Task MissingConstRows_ResolveToZeroAndAreNotCraftOrderSkills()
    {
        await Assert.That(CraftOrderContent.IsCraftOrderSkill(0)).IsFalse();
        await Assert.That(CraftOrderContent.IsMakeSheetSkill(0)).IsFalse();
        await Assert.That(CraftOrderContent.IsRestoreSheetSkill(0)).IsFalse();
    }
}
