using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_Default_CreatesItemWithDefaultValues()
    {
        // Act
        var item = new Item();

        // Assert
        Assert.NotNull(item);
        Assert.Equal(0u, item.Id);
        Assert.Equal(0u, item.TemplateId);
        Assert.Equal(0, item.Count);
        Assert.Equal(-1, item.Slot);
        Assert.Equal(0, item.Grade);
        Assert.Null(item.Template);
        Assert.Equal(ItemFlag.None, item.ItemFlags);
    }

    [Fact]
    public void Constructor_WithWorldId_SetsWorldId()
    {
        // Arrange
        byte worldId = 1;

        // Act
        var item = new Item(worldId);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(worldId, item.WorldId);
    }

    [Fact]
    public void Constructor_WithIdTemplateAndCount_CreatesItemWithValues()
    {
        // Arrange
        ulong id = 1000;
        var template = new ItemTemplate { Id = 100, Name = "Test Item" };
        int count = 5;

        // Act
        var item = new Item(id, template, count);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(id, item.Id);
        Assert.Equal(template.Id, item.TemplateId);
        Assert.Equal(template, item.Template);
        Assert.Equal(count, item.Count);
        Assert.Equal(-1, item.Slot);
    }

    [Fact]
    public void Constructor_WithAllParameters_CreatesItemWithAllValues()
    {
        // Arrange
        byte worldId = 2;
        ulong id = 2000;
        var template = new ItemTemplate { Id = 200, Name = "Another Item" };
        int count = 10;

        // Act
        var item = new Item(worldId, id, template, count);

        // Assert
        Assert.NotNull(item);
        Assert.Equal(worldId, item.WorldId);
        Assert.Equal(id, item.Id);
        Assert.Equal(template.Id, item.TemplateId);
        Assert.Equal(template, item.Template);
        Assert.Equal(count, item.Count);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Grade_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Grade = 5;

        // Assert
        Assert.Equal(5, item.Grade);
    }

    [Fact]
    public void ItemFlags_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ItemFlags = ItemFlag.SoulBound;

        // Assert
        Assert.Equal(ItemFlag.SoulBound, item.ItemFlags);
    }

    [Fact]
    public void ItemFlags_CanCombineFlags()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ItemFlags = ItemFlag.SoulBound | ItemFlag.HasUCC;

        // Assert
        Assert.Equal(ItemFlag.SoulBound | ItemFlag.HasUCC, item.ItemFlags);
    }

    [Fact]
    public void LifespanMins_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.LifespanMins = 60;

        // Assert
        Assert.Equal(60, item.LifespanMins);
    }

    [Fact]
    public void UccId_SetValue_SetsHasUCCFlag()
    {
        // Arrange
        var item = new Item();

        // Act
        item.UccId = 123;

        // Assert
        Assert.Equal(123ul, item.UccId);
        Assert.True(item.ItemFlags.HasFlag(ItemFlag.HasUCC));
    }

    [Fact]
    public void UccId_SetZero_RemovesHasUCCFlag()
    {
        // Arrange
        var item = new Item { UccId = 123 };

        // Act
        item.UccId = 0;

        // Assert
        Assert.Equal(0ul, item.UccId);
        Assert.False(item.ItemFlags.HasFlag(ItemFlag.HasUCC));
    }

    [Fact]
    public void ExpirationTime_SetValue_UpdatesValue()
    {
        // Arrange
        var item = new Item();
        var expirationTime = DateTime.UtcNow.AddDays(7);

        // Act
        item.ExpirationTime = expirationTime;

        // Assert
        Assert.Equal(expirationTime, item.ExpirationTime);
    }

    [Fact]
    public void ExpirationOnlineMinutesLeft_SetValue_UpdatesValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ExpirationOnlineMinutesLeft = 120.5;

        // Assert
        Assert.Equal(120.5, item.ExpirationOnlineMinutesLeft);
    }

    [Fact]
    public void ChargeCount_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();

        // Act
        item.ChargeCount = 10;

        // Assert
        Assert.Equal(10, item.ChargeCount);
    }

    [Fact]
    public void ChargeStartTime_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var item = new Item();
        var startTime = DateTime.UtcNow;

        // Act
        item.ChargeStartTime = startTime;

        // Assert
        Assert.Equal(startTime, item.ChargeStartTime);
    }

    #endregion

    #region Flag Manipulation Tests

    [Fact]
    public void SetFlag_AddsFlag()
    {
        // Arrange
        var item = new Item();

        // Act
        item.SetFlag(ItemFlag.SoulBound);

        // Assert
        Assert.True(item.ItemFlags.HasFlag(ItemFlag.SoulBound));
    }

    [Fact]
    public void RemoveFlag_RemovesFlag()
    {
        // Arrange
        var item = new Item { ItemFlags = ItemFlag.SoulBound | ItemFlag.HasUCC };

        // Act
        item.RemoveFlag(ItemFlag.HasUCC);

        // Assert
        Assert.False(item.ItemFlags.HasFlag(ItemFlag.HasUCC));
        Assert.True(item.ItemFlags.HasFlag(ItemFlag.SoulBound));
    }

    #endregion

    #region Static Properties Tests

    [Fact]
    public void DawnStone_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(327u, Item.DawnStone);
    }

    [Fact]
    public void Coins_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(500u, Item.Coins);
    }

    [Fact]
    public void TaxCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(31891u, Item.TaxCertificate);
    }

    [Fact]
    public void BoundTaxCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(31892u, Item.BoundTaxCertificate);
    }

    [Fact]
    public void AppraisalCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(28085u, Item.AppraisalCertificate);
    }

    [Fact]
    public void CrestStamp_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(17662u, Item.CrestStamp);
    }

    [Fact]
    public void CrestInk_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(17663u, Item.CrestInk);
    }

    [Fact]
    public void SheetMusic_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(28051u, Item.SheetMusic);
    }

    [Fact]
    public void SalonCertificate_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(30811u, Item.SalonCertificate);
    }

    [Fact]
    public void TreasureMapWithCoordinates_ReturnsCorrectId()
    {
        // Act & Assert
        Assert.Equal(24581u, Item.TreasureMapWithCoordinates);
    }

    #endregion

    #region IsDirty Tests

    [Fact]
    public void IsDirty_Default_IsTrue()
    {
        // Arrange
        var item = new Item();

        // Assert
        Assert.True(item.IsDirty);
    }

    [Fact]
    public void IsDirty_SetFalse_BecomesFalse()
    {
        // Arrange
        var item = new Item();

        // Act
        item.IsDirty = false;

        // Assert
        Assert.False(item.IsDirty);
    }

    [Fact]
    public void SettingGrade_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Grade = 3;

        // Assert
        Assert.True(item.IsDirty);
    }

    [Fact]
    public void SettingCount_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Count = 100;

        // Assert
        Assert.True(item.IsDirty);
    }

    [Fact]
    public void SettingSlot_SetsIsDirtyTrue()
    {
        // Arrange
        var item = new Item { IsDirty = false };

        // Act
        item.Slot = 5;

        // Assert
        Assert.True(item.IsDirty);
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_NullItem_ReturnsOne()
    {
        // Arrange
        var item = new Item();

        // Act
        var result = item.CompareTo(null);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void CompareTo_ItemWithLowerSlot_ReturnsNegative()
    {
        // Arrange
        var item1 = new Item { Slot = 5 };
        var item2 = new Item { Slot = 10 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        Assert.True(result < 0);
    }

    [Fact]
    public void CompareTo_ItemWithHigherSlot_ReturnsPositive()
    {
        // Arrange
        var item1 = new Item { Slot = 10 };
        var item2 = new Item { Slot = 5 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        Assert.True(result > 0);
    }

    [Fact]
    public void CompareTo_ItemWithSameSlot_ReturnsZero()
    {
        // Arrange
        var item1 = new Item { Slot = 5 };
        var item2 = new Item { Slot = 5 };

        // Act
        var result = item1.CompareTo(item2);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Count_CanBeZero()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Count = 0;

        // Assert
        Assert.Equal(0, item.Count);
    }

    [Fact]
    public void Count_CanBeNegative()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Count = -1;

        // Assert
        Assert.Equal(-1, item.Count);
    }

    [Fact]
    public void Grade_CanBeMaxByte()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Grade = byte.MaxValue;

        // Assert
        Assert.Equal(byte.MaxValue, item.Grade);
    }

    [Fact]
    public void Id_CanBeMaxUInt64()
    {
        // Arrange
        var item = new Item();

        // Act
        item.Id = ulong.MaxValue;

        // Assert
        Assert.Equal(ulong.MaxValue, item.Id);
    }

    [Fact]
    public void TemplateId_CanBeMaxUInt32()
    {
        // Arrange
        var item = new Item();

        // Act
        item.TemplateId = uint.MaxValue;

        // Assert
        Assert.Equal(uint.MaxValue, item.TemplateId);
    }

    #endregion
}
