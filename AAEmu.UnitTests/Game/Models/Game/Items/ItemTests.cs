using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Trading;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

public class ItemTests
{
    [Test]
    [Arguments(ItemDetailType.BackpackFreshness, 10)]
    [Arguments(ItemDetailType.Unknown13, 13)]
    [Arguments(ItemDetailType.Unknown14, 8)]
    public async Task GenericDetails_RoundTripExactBytes(ItemDetailType detailType, int detailLength)
    {
        var expected = Enumerable.Range(1, detailLength).Select(x => (byte)x).ToArray();
        var item = new Item
        {
            DetailType = detailType,
            Detail = expected
        };
        var persisted = new PacketStream();

        item.WriteDetails(persisted);
        var loaded = new Item { DetailType = detailType };
        loaded.ReadDetails((PacketStream)persisted.GetBytes());

        await Assert.That(loaded.Detail).IsEquivalentTo(expected);
    }

    [Test]
    public async Task CopyPersistentStateFrom_ClonesOpaqueDetailAndProvenance()
    {
        var source = new Item
        {
            WorldId = 3,
            MadeUnitId = 42,
            CreateTime = new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc),
            DetailType = ItemDetailType.Unknown13,
            Detail = Enumerable.Range(1, 13).Select(value => (byte)value).ToArray()
        };
        var split = new Item();

        split.CopyPersistentStateFrom(source);

        await Assert.That(split.WorldId).IsEqualTo(source.WorldId);
        await Assert.That(split.MadeUnitId).IsEqualTo(source.MadeUnitId);
        await Assert.That(split.CreateTime).IsEqualTo(source.CreateTime);
        await Assert.That(split.DetailType).IsEqualTo(source.DetailType);
        await Assert.That(split.Detail).IsEquivalentTo(source.Detail);
        await Assert.That(ReferenceEquals(split.Detail, source.Detail)).IsFalse();
    }

    [Test]
    public async Task CanStackWith_RequiresMatchingDetailAndProvenance()
    {
        var template = new ItemTemplate { Id = 100, MaxCount = 100 };
        var left = new Item(1, template, 1)
        {
            MadeUnitId = 42,
            DetailType = ItemDetailType.Unknown14,
            Detail = Enumerable.Range(1, 8).Select(value => (byte)value).ToArray()
        };
        var matching = new Item(2, template, 1)
        {
            MadeUnitId = 42,
            DetailType = ItemDetailType.Unknown14,
            Detail = left.Detail.ToArray()
        };
        var differentDetail = new Item(3, template, 1)
        {
            MadeUnitId = 42,
            DetailType = ItemDetailType.Unknown14,
            Detail = new byte[8]
        };
        var differentProducer = new Item(4, template, 1)
        {
            MadeUnitId = 43,
            DetailType = ItemDetailType.Unknown14,
            Detail = left.Detail.ToArray()
        };

        await Assert.That(left.CanStackWith(matching)).IsTrue();
        await Assert.That(left.CanStackWith(differentDetail)).IsFalse();
        await Assert.That(left.CanStackWith(differentProducer)).IsFalse();
    }

    [Test]
    public async Task BackpackFreshness_UsesCanonicalTenByteDetail()
    {
        var freshnessStart = new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc);
        var backpack = new Backpack(42, new BackpackTemplate { Id = 31840 }, 1);
        var expected = new PacketStream();
        expected.Write(freshnessStart);
        expected.Write((ushort)22);

        backpack.InitializeFreshness(freshnessStart, 22);
        var hasFreshness = backpack.TryGetFreshness(out var actualStart, out var actualZoneGroupId);

        await Assert.That(backpack.DetailType).IsEqualTo(ItemDetailType.BackpackFreshness);
        await Assert.That(backpack.Detail).IsEquivalentTo(expected.GetBytes());
        await Assert.That(backpack.Detail.Length).IsEqualTo(10);
        await Assert.That(hasFreshness).IsTrue();
        await Assert.That(actualStart).IsEqualTo(freshnessStart);
        await Assert.That(actualZoneGroupId).IsEqualTo((ushort)22);
    }

    [Test]
    public async Task BackpackFreshness_DoesNotOverwriteExistingDetail()
    {
        var backpack = new Backpack(42, new BackpackTemplate { Id = 31840 }, 1);
        backpack.InitializeFreshness(new DateTime(2026, 8, 30, 12, 34, 56, DateTimeKind.Utc), 22);

        await Assert.That(() => backpack.InitializeFreshness(DateTime.UtcNow, 8))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BackpackFreshness_RejectsMalformedDetail()
    {
        var backpack = new Backpack
        {
            DetailType = ItemDetailType.BackpackFreshness,
            Detail = new byte[9]
        };

        var result = backpack.TryGetFreshness(out _, out _);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task SpecialtyPackMaterializer_AppliesSourceContextOnce()
    {
        var template = new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7,
            SpecialtyZoneId = 22
        };
        var backpack = new Backpack(42, template, 1);
        var freshnessStart = new DateTime(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc);
        var context = new SpecialtyPackProductionContext(
            SpecialtyPackProductionSource.Craft,
            freshnessStart,
            22,
            1234);

        var materialized = SpecialtyPackMaterializer.TryMaterialize(backpack, context);
        var repeated = SpecialtyPackMaterializer.TryMaterialize(backpack, context);
        var hasFreshness = backpack.TryGetFreshness(out var actualStart, out var actualZone);

        await Assert.That(materialized).IsTrue();
        await Assert.That(repeated).IsFalse();
        await Assert.That(backpack.MadeUnitId).IsEqualTo(1234u);
        await Assert.That(hasFreshness).IsTrue();
        await Assert.That(actualStart).IsEqualTo(freshnessStart);
        await Assert.That(actualZone).IsEqualTo((ushort)22);
    }

    [Test]
    public async Task SpecialtyPackMaterializer_RejectsMissingOrMismatchedSourceContext()
    {
        var template = new BackpackTemplate
        {
            Id = 31840,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 7,
            SpecialtyZoneId = 22
        };
        var backpack = new Backpack(42, template, 1);
        var mismatchedZone = new SpecialtyPackProductionContext(
            SpecialtyPackProductionSource.DoodadLootItem,
            new DateTime(2026, 8, 31, 12, 34, 56, DateTimeKind.Utc),
            8,
            0);

        await Assert.That(SpecialtyPackMaterializer.TryMaterialize(backpack, null)).IsFalse();
        await Assert.That(SpecialtyPackMaterializer.TryMaterialize(backpack, mismatchedZone)).IsFalse();
        await Assert.That(backpack.HasDefaultDetail).IsTrue();
        await Assert.That(backpack.MadeUnitId).IsEqualTo(0u);
    }

    [Test]
    public async Task SpecialtyPackMaterializer_LeavesNonFreshBackpackUnchanged()
    {
        var template = new BackpackTemplate
        {
            Id = 32000,
            BackpackType = BackpackType.TradePack,
            FreshnessGroupId = 0
        };
        var backpack = new Backpack(42, template, 1);

        var materialized = SpecialtyPackMaterializer.TryMaterialize(backpack, null);

        await Assert.That(materialized).IsTrue();
        await Assert.That(backpack.HasDefaultDetail).IsTrue();
    }

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
    public async Task BmMileage_ReturnsCorrectId()
    {
        await Assert.That(Item.BmMileage).IsEqualTo(28586u);
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

    [Test]
    public async Task SettingDetailState_SetsIsDirtyTrue()
    {
        var item = new Item { IsDirty = false };

        item.DetailType = ItemDetailType.BackpackFreshness;

        await Assert.That(item.IsDirty).IsTrue();
        item.IsDirty = false;

        item.Detail = [1, 2, 3];

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
