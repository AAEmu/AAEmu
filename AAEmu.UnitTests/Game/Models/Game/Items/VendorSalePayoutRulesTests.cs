using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// What a vendor sale batch pays out.
/// </summary>
/// <remarks>
/// The sell handler moves each item to the buy-back list and pays once for the whole batch, so the
/// one property that matters is that money is owed only for items the player no longer holds. Getting
/// that wrong is not a cosmetic bug in either direction: paying for an item that never left the bag
/// hands out money for nothing, and not paying for one that did leaves it in the vendor's list unpaid.
/// </remarks>
public class VendorSalePayoutRulesTests
{
    private const int Refund = 1000;
    private const float Multiplier = 100f;   // the grade's full refund percentage

    [Test]
    public async Task AMovedItemPaysForItself()
    {
        var value = VendorSalePayoutRules.LineValue(Refund, Multiplier, 3, ItemSaleTransfer.Moved);

        await Assert.That(value).IsEqualTo(3000);
    }

    /// <summary>The regression: an item that never left the bag is not something a vendor took.</summary>
    [Test]
    public async Task AnItemThatDidNotMovePaysNothing()
    {
        var value = VendorSalePayoutRules.LineValue(Refund, Multiplier, 3, ItemSaleTransfer.NotMoved);

        await Assert.That(value).IsEqualTo(0);
    }

    /// <summary>
    /// The batch is the real shape of the bug: some lines moved and some did not, and the total must
    /// be exactly the moved ones. A handler that added a value per attempted item gets this wrong by
    /// the whole value of the unmoved line.
    /// </summary>
    [Test]
    public async Task ABatchPaysOnlyForTheLinesThatMoved()
    {
        var lines = new (int, float, int, ItemSaleTransfer)[]
        {
            (Refund, Multiplier, 2, ItemSaleTransfer.Moved),
            (Refund, Multiplier, 5, ItemSaleTransfer.NotMoved),
            (250, Multiplier, 1, ItemSaleTransfer.Moved),
        };

        var total = VendorSalePayoutRules.BatchValue(lines);

        // 2000 from the first line plus 250 from the third; the five-count line pays nothing.
        await Assert.That(total).IsEqualTo(2250);
    }

    /// <summary>
    /// Swept, because a batch rule that keyed off the wrong line would pass a single mixed case where
    /// the unmoved line happens to be first or last. Every position of an unmoved line must give the
    /// same answer.
    /// </summary>
    [Test]
    public async Task ThePositionOfAnUnmovedLineDoesNotChangeTheTotal()
    {
        for (var position = 0; position < 3; position++)
        {
            var lines = new List<(int, float, int, ItemSaleTransfer)>
            {
                (Refund, Multiplier, 1, ItemSaleTransfer.Moved),
                (Refund, Multiplier, 2, ItemSaleTransfer.Moved),
            };
            lines.Insert(position, (Refund, Multiplier, 9, ItemSaleTransfer.NotMoved));

            var total = VendorSalePayoutRules.BatchValue(lines);

            await Assert.That(total).IsEqualTo(3000);
        }
    }

    /// <summary>A batch where nothing moved pays nothing, rather than a total of failed lines.</summary>
    [Test]
    public async Task ABatchWhereNothingMovedPaysNothing()
    {
        var lines = new (int, float, int, ItemSaleTransfer)[]
        {
            (Refund, Multiplier, 4, ItemSaleTransfer.NotMoved),
            (Refund, Multiplier, 7, ItemSaleTransfer.NotMoved),
        };

        await Assert.That(VendorSalePayoutRules.BatchValue(lines)).IsEqualTo(0);
    }

    /// <summary>An empty batch is a real answer - nothing was sold - and not an error.</summary>
    [Test]
    public async Task AnEmptyBatchPaysNothing()
    {
        await Assert.That(
            VendorSalePayoutRules.BatchValue(Array.Empty<(int, float, int, ItemSaleTransfer)>()))
            .IsEqualTo(0);
    }

    /// <summary>The grade's percentage still applies, so a discount is honoured per line.</summary>
    [Test]
    public async Task TheGradePercentageAppliesToEachMovedLine()
    {
        var lines = new (int, float, int, ItemSaleTransfer)[]
        {
            (1000, 50f, 1, ItemSaleTransfer.Moved),
            (1000, 50f, 1, ItemSaleTransfer.Moved),
        };

        await Assert.That(VendorSalePayoutRules.BatchValue(lines)).IsEqualTo(1000);
    }
}
