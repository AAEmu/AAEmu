using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Crafts;

/// <summary>
/// The sheet half of the craft order slice: how a cast's material bill scales, and how a sheet carries
/// the craft it stands for.
/// </summary>
public class CraftOrderSheetRulesTests
{
    [Test]
    public async Task MaterialCost_ScalesWithTheRequestedCount()
    {
        await Assert.That(CraftOrderSheetRules.MaterialCost(10, 1)).IsEqualTo(10L);
        await Assert.That(CraftOrderSheetRules.MaterialCost(10, 3)).IsEqualTo(30L);
        await Assert.That(CraftOrderSheetRules.MaterialCost(1, 0)).IsEqualTo(0L);
    }

    [Test]
    public async Task MaterialCost_DoesNotWrapOnAnAbsurdCount()
    {
        // 2^31 materials per run times a huge count must not come back as a small positive number.
        var cost = CraftOrderSheetRules.MaterialCost(int.MaxValue, uint.MaxValue);

        await Assert.That(cost).IsGreaterThan((long)int.MaxValue);
        await Assert.That(CraftOrderSheetRules.IsConsumable(cost)).IsFalse();
    }

    [Test]
    public async Task MaterialCost_TreatsANegativeRowAsNoCost()
    {
        await Assert.That(CraftOrderSheetRules.MaterialCost(-5, 4)).IsEqualTo(0L);
    }

    [Test]
    public async Task UsableCount_NeedsAtLeastOne()
    {
        await Assert.That(CraftOrderSheetRules.IsUsableCount(0)).IsFalse();
        await Assert.That(CraftOrderSheetRules.IsUsableCount(1)).IsTrue();
        await Assert.That(CraftOrderSheetRules.IsUsableCount(uint.MaxValue)).IsTrue();
    }

    [Test]
    public async Task Consumable_AcceptsOnlyWhatAContainerCanHold()
    {
        await Assert.That(CraftOrderSheetRules.IsConsumable(0)).IsFalse();
        await Assert.That(CraftOrderSheetRules.IsConsumable(1)).IsTrue();
        await Assert.That(CraftOrderSheetRules.IsConsumable(int.MaxValue)).IsTrue();
        await Assert.That(CraftOrderSheetRules.IsConsumable((long)int.MaxValue + 1)).IsFalse();
    }

    [Test]
    public async Task GradeOf_UsesTheProductGradeOnlyWhenTheCraftAsksForOne()
    {
        var plain = new Craft();
        plain.CraftProducts.Add(new CraftProduct { UseGrade = false, ItemGradeId = 4 });
        var graded = new Craft();
        graded.CraftProducts.Add(new CraftProduct { UseGrade = true, ItemGradeId = 4 });

        await Assert.That(CraftOrderSheetRules.GradeOf(plain)).IsEqualTo((byte)0);
        await Assert.That(CraftOrderSheetRules.GradeOf(graded)).IsEqualTo((byte)4);
        await Assert.That(CraftOrderSheetRules.GradeOf(new Craft())).IsEqualTo((byte)0);
        await Assert.That(CraftOrderSheetRules.GradeOf(null)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Sheet_CarriesTheCraftGradeCountAndActabilityThroughItsDetailBlock()
    {
        var written = new CraftOrderSheetItem();
        written.SetOrder(5591, 0, 2, 33);
        var stream = new PacketStream();
        written.WriteDetails(stream);

        var body = stream.GetBytes();
        await Assert.That(body.Length).IsEqualTo(13);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(5591u);
        await Assert.That(body[4]).IsEqualTo((byte)0);
        await Assert.That(BitConverter.ToUInt32(body, 5)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt32(body, 9)).IsEqualTo(33u);

        var read = new CraftOrderSheetItem();
        read.ReadDetails(new PacketStream(body));
        await Assert.That(read.CraftId).IsEqualTo(5591u);
        await Assert.That(read.CraftGrade).IsEqualTo((byte)0);
        await Assert.That(read.CraftCount).IsEqualTo(2u);
        await Assert.That(read.ActabilityGroupId).IsEqualTo(33u);
    }

    [Test]
    public async Task Sheet_IgnoresATruncatedDetailBlock()
    {
        var read = new CraftOrderSheetItem { CraftId = 7, CraftCount = 9 };
        read.ReadDetails(new PacketStream(new byte[] { 1, 2, 3 }));

        await Assert.That(read.CraftId).IsEqualTo(7u);
        await Assert.That(read.CraftCount).IsEqualTo(9u);
    }

    [Test]
    public async Task Sheet_DetailTypeIsTheCraftOrderBlock()
    {
        var sheet = new CraftOrderSheetItem();

        await Assert.That(sheet.DetailType).IsEqualTo(ItemDetailType.CraftOrderSheet);
        await Assert.That(sheet.DetailBytesLength).IsEqualTo(13u);
    }

    [Test]
    public async Task RestoreMaterials_IsTheSameBillMakingTheSheetTook()
    {
        var craft = new Craft();
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 10, Amount = 2 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 11, Amount = 5 });

        var bill = CraftOrderSheetRules.RestoreMaterials(craft, 3);

        await Assert.That(bill.Count).IsEqualTo(2);
        await Assert.That(bill[0]).IsEqualTo((10u, 6));
        await Assert.That(bill[1]).IsEqualTo((11u, 15));
    }

    [Test]
    public async Task RestoreMaterials_IsEmptyWhenThereIsNothingToGiveBack()
    {
        await Assert.That(CraftOrderSheetRules.RestoreMaterials(null, 1)).IsEmpty();
        await Assert.That(CraftOrderSheetRules.RestoreMaterials(new Craft(), 1)).IsEmpty();
        var craft = new Craft();
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 10, Amount = 2 });
        await Assert.That(CraftOrderSheetRules.RestoreMaterials(craft, 0)).IsEmpty();
    }

    [Test]
    public async Task RestoreActionKind_ClearsTheRestoreTab()
    {
        await Assert.That(CraftOrderSheetRules.RestoreActionKind).IsEqualTo((byte)3);
    }

    [Test]
    public async Task RequestNamesASheet_OnlyABagSheet()
    {
        await Assert.That(CraftOrderSheetRules.RequestNamesASheet(new CraftOrderSheetItem())).IsTrue();
        await Assert.That(CraftOrderSheetRules.RequestNamesASheet(null)).IsFalse();
    }

    [Test]
    public async Task MaterialRows_AreTheRestoreBillOnTheWire()
    {
        var craft = new Craft();
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 10, Amount = 2 });
        craft.CraftMaterials.Add(new CraftMaterial { ItemId = 11, Amount = 5 });

        var rows = CraftOrderSheetRules.MaterialRows(craft, 3);

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0]).IsEqualTo(new CraftOrderMaterialRow(10, 0, 6));
        await Assert.That(rows[1]).IsEqualTo(new CraftOrderMaterialRow(11, 0, 15));
        await Assert.That(CraftOrderSheetRules.MaterialRows(craft, 1)[0].Stack).IsEqualTo(2u);
        await Assert.That(CraftOrderSheetRules.MaterialRows(null, 1)).IsEmpty();
    }
}
