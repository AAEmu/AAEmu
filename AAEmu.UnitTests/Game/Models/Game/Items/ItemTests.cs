using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_Default_CreatesItemWithDefaultValues()
    {
        // Act
        var item = new Item();

        // Assert
        await Assert.That(item).IsNotNull();
        await Assert.That(item.Id).IsEqualTo(0u);
        await Assert.That(item.TemplateId).IsEqualTo(0u);
        await Assert.That(item.Count).IsEqualTo(0);
        await Assert.That(item.Slot).IsEqualTo(-1);
        await Assert.That(item.Grade).IsEqualTo((byte)0);
        await Assert.That(item.Template).IsNull();
        await Assert.That(item.ItemFlags).IsEqualTo(ItemFlag.None);
    }

    [Test]
    public async Task Constructor_WithWorldId_SetsWorldId()
    {
        // Arrange
        byte worldId = 1;

        // Act
        var item = new Item(worldId);

        // Assert
        await Assert.That(item).IsNotNull();
        await Assert.That(item.WorldId).IsEqualTo(worldId);
    }

    [Test]
    public async Task Constructor_WithIdTemplateAndCount_CreatesItemWithValues()
    {
        // Arrange
        ulong id = 1000;
        var template = new ItemTemplate { Id = 100, Name = "Test Item" };
        int count = 5;

        // Act
        var item = new Item(id, template, count);

        // Assert
        await Assert.That(item).IsNotNull();
        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.TemplateId).IsEqualTo(template.Id);
        await Assert.That(item.Template).IsEqualTo(template);
        await Assert.That(item.Count).IsEqualTo(count);
        await Assert.That(item.Slot).IsEqualTo(-1);
    }

    [Test]
    public async Task Constructor_WithAllParameters_CreatesItemWithAllValues()
    {
        // Arrange
        byte worldId = 2;
        ulong id = 2000;
        var template = new ItemTemplate { Id = 200, Name = "Another Item" };
        int count = 10;

        // Act
        var item = new Item(worldId, id, template, count);

        // Assert
        await Assert.That(item).IsNotNull();
        await Assert.That(item.WorldId).IsEqualTo(worldId);
        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.TemplateId).IsEqualTo(template.Id);
        await Assert.That(item.Template).IsEqualTo(template);
        await Assert.That(item.Count).IsEqualTo(count);
    }

    #endregion

    #region Property Tests

    [Test]
    public async Task Grade_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Grade = 5;

        // Assert
        await Assert.That(item.Grade).IsEqualTo((byte)5);
    }

    [Test]
    public async Task ItemFlags_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ItemFlags = ItemFlag.SoulBound;

        // Assert
        await Assert.That(item.ItemFlags).IsEqualTo(ItemFlag.SoulBound);
    }

    [Test]
    public async Task ItemFlags_CanCombineFlags()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ItemFlags = ItemFlag.SoulBound | ItemFlag.HasUCC;

        // Assert
        await Assert.That(item.ItemFlags).IsEqualTo(ItemFlag.SoulBound | ItemFlag.HasUCC);
    }

    [Test]
    public async Task LifespanMins_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.LifespanMins = 60;

        // Assert
        await Assert.That(item.LifespanMins).IsEqualTo(60);
    }

    [Test]
    public async Task UccId_SetValue_SetsHasUCCFlag()
    {
        // Arrange
        var item = new Item();

        // Act
        item.UccId = 123;

        // Assert
        await Assert.That(item.UccId).IsEqualTo(123ul);
        await Assert.That(item.ItemFlags.HasFlag(ItemFlag.HasUCC)).IsTrue();
    }

    [Test]
    public async Task UccId_SetZero_RemovesHasUCCFlag()
    {
        // Arrange
        var item = new Item { UccId = 123 };

        // Act
        item.UccId = 0;

        // Assert
        await Assert.That(item.UccId).IsEqualTo(0ul);
        await Assert.That(item.ItemFlags.HasFlag(ItemFlag.HasUCC)).IsFalse();
    }

    [Test]
    public async Task ExpirationTime_SetValue_UpdatesValue()
    {
        // Arrange
        var item = new Item();
        var expirationTime = DateTime.UtcNow.AddDays(7);

        // Act
        item.ExpirationTime = expirationTime;

        // Assert
        await Assert.That(item.ExpirationTime).IsEqualTo(expirationTime);
    }

    [Test]
    public async Task ExpirationOnlineMinutesLeft_SetValue_UpdatesValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ExpirationOnlineMinutesLeft = 120.5;

        // Assert
        await Assert.That(item.ExpirationOnlineMinutesLeft).IsEqualTo(120.5);
    }

    [Test]
    public async Task ChargeCount_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ChargeCount = 10;

        // Assert
        await Assert.That(item.ChargeCount).IsEqualTo(10);
    }

    [Test]
    public async Task ChargeStartTime_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();
        var startTime = DateTime.UtcNow;

        // Act
        item.ChargeStartTime = startTime;

        // Assert
        await Assert.That(item.ChargeStartTime).IsEqualTo(startTime);
    }

    #endregion

    #region Flag Manipulation Tests

    [Test]
    public async Task SetFlag_AddsFlag()
    {
        // Arrange
        var item = new Item();

        // Act
        item.SetFlag(ItemFlag.SoulBound);

        // Assert
        await Assert.That(item.ItemFlags.HasFlag(ItemFlag.SoulBound)).IsTrue();
    }

    [Test]
    public async Task RemoveFlag_RemovesFlag()
    {
        // Arrange
        var item = new Item { ItemFlags = ItemFlag.SoulBound | ItemFlag.HasUCC };

        // Act
        item.RemoveFlag(ItemFlag.HasUCC);

        // Assert
        await Assert.That(item.ItemFlags.HasFlag(ItemFlag.HasUCC)).IsFalse();
        await Assert.That(item.ItemFlags.HasFlag(ItemFlag.SoulBound)).IsTrue();
    }

    #endregion

    #region Static Properties Tests

    [Test]
    public async Task DawnStone_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.DawnStone).IsEqualTo(327u);
    }

    [Test]
    public async Task Coins_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.Coins).IsEqualTo(500u);
    }

    [Test]
    public async Task TaxCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.TaxCertificate).IsEqualTo(31891u);
    }

    [Test]
    public async Task BoundTaxCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.BoundTaxCertificate).IsEqualTo(31892u);
    }

    [Test]
    public async Task AppraisalCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.AppraisalCertificate).IsEqualTo(28085u);
    }

    [Test]
    public async Task CrestStamp_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.CrestStamp).IsEqualTo(17662u);
    }

    [Test]
    public async Task CrestInk_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.CrestInk).IsEqualTo(17663u);
    }

    [Test]
    public async Task SheetMusic_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.SheetMusic).IsEqualTo(28051u);
    }

    [Test]
    public async Task SalonCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.SalonCertificate).IsEqualTo(30811u);
    }

    [Test]
    public async Task TreasureMapWithCoordinates_ReturnsCorrectId()
    {
        // Act & Assert
        await Assert.That(Item.TreasureMapWithCoordinates).IsEqualTo(24581u);
    }

    #endregion

    #region IsDirty Tests

    [Test]
    public async Task IsDirty_Default_IsTrue()
    {
        // Arrange
        var item = new Item();

        // Assert
        await Assert.That(item.IsDirty).IsTrue();
    }

    [Test]
    public async Task IsDirty_SetFalse_BecomesFalse()
    {
        // Arrange
        var item = new Item();

        // Act
        item.IsDirty = false;

        // Assert
        await Assert.That(item.IsDirty).IsFalse();
    }

    [Test]
    public async Task SettingGrade_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Grade = 3;

        // Assert
        await Assert.That(item.IsDirty).IsTrue();
    }

    [Test]
    public async Task SettingCount_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Count = 100;

        // Assert
        await Assert.That(item.IsDirty).IsTrue();
    }

    [Test]
    public async Task SettingSlot_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Slot = 5;

        // Assert
        await Assert.That(item.IsDirty).IsTrue();
    }

    #endregion

    #region CompareTo Tests

    [Test]
    public async Task CompareTo_NullItem_ReturnsOne()
    {
        // Arrange
        var item = new Item();

        // Act
        var result = item.CompareTo(null);

        // Assert
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task CompareTo_ItemWithLowerSlot_ReturnsNegative()
    {
        // Arrange
        var item1 = new Item { Slot = 5 };
        var item2 = new Item { Slot = 10 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        await Assert.That(result < 0).IsTrue();
    }

    [Test]
    public async Task CompareTo_ItemWithHigherSlot_ReturnsPositive()
    {
        // Arrange
        var item1 = new Item { Slot = 10 };
        var item2 = new Item { Slot = 5 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        await Assert.That(result > 0).IsTrue();
    }

    [Test]
    public async Task CompareTo_ItemWithSameSlot_ReturnsZero()
    {
        // Arrange
        var item1 = new Item { Slot = 5 };
        var item2 = new Item { Slot = 5 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        await Assert.That(result).IsEqualTo(0);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Count_CanBeZero()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Count = 0;

        // Assert
        await Assert.That(item.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Count_CanBeNegative()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Count = -1;

        // Assert
        await Assert.That(item.Count).IsEqualTo(-1);
    }

    [Test]
    public async Task Grade_CanBeMaxByte()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Grade = byte.MaxValue;

        // Assert
        await Assert.That(item.Grade).IsEqualTo(byte.MaxValue);
    }

    [Test]
    public async Task Id_CanBeMaxUInt64()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Id = ulong.MaxValue;

        // Assert
        await Assert.That(item.Id).IsEqualTo(ulong.MaxValue);
    }

    [Test]
    public async Task TemplateId_CanBeMaxUInt32()
    {
        // Arrange
        var item = new Item();

        // Act
        item.TemplateId = uint.MaxValue;

        // Assert
        await Assert.That(item.TemplateId).IsEqualTo(uint.MaxValue);
    }

    #endregion
}
