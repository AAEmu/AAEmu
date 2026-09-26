using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCButlerPacketTests
{
    [Test]
    public async Task InitInfo_WritesHouseNameThenCompletePopulatedButlerState()
    {
        var info = PopulatedInfo();
        var stream = new SCButlerInitInfoPacket("Dawn's Cottage", info).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadString()).IsEqualTo("Dawn's Cottage");
        await AssertInfo(stream, info);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Bound_WritesNativeEmptyStateThenHouseAndErrorMessage()
    {
        var info = ButlerInfoWire.Empty(0, sbyte.MaxValue, string.Empty, 0, 1, ushort.MaxValue, 0);
        var stream = new SCButlerBoundPacket(info, string.Empty, ushort.MaxValue).Write(new PacketStream());

        stream.Rollback();
        await AssertInfo(stream, info);
        await Assert.That(stream.ReadString()).IsEqualTo(string.Empty);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InfoUpdated_WritesEachVerifiedFlagSelectedGroupInOrder()
    {
        var actabilities = new[] { new ButlerActabilityWire(9, uint.MaxValue, short.MinValue) };
        var permanentDatas = new Dictionary<sbyte, ulong> { [3] = 30, [-1] = ulong.MaxValue };
        var unitAttributes = new Dictionary<uint, uint> { [7] = uint.MaxValue, [3] = 4 };
        var stream = new SCButlerInfoUpdatedPacket(ushort.MaxValue, 0x3f, true, false, actabilities, permanentDatas,
            uint.MaxValue, 0, ushort.MaxValue, "Updated 한", unitAttributes).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0x3f);
        await Assert.That(stream.ReadBoolean()).IsTrue();
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadPisc(2)).IsEquivalentTo(new uint[] { 9, uint.MaxValue });
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MinValue);
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)-1);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ulong.MaxValue);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)3);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(30UL);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.ReadString()).IsEqualTo("Updated 한");
        await Assert.That(stream.ReadInt32()).IsEqualTo(2);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(3u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(4u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InfoUpdated_ZeroFlagsWritesOnlyTheHeader()
    {
        var stream = new SCButlerInfoUpdatedPacket(1, 0, false, true,
            [new ButlerActabilityWire(1, 2, 3)], new Dictionary<sbyte, ulong> { [1] = 2 }, 3, 4, 5, "ignored",
            new Dictionary<uint, uint> { [6] = 7 }).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)1);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task InfoUpdated_LaborPowerFlagWritesOnlyLaborPowerAndChargedAmount()
    {
        var stream = new SCButlerInfoUpdatedPacket(1, 0x04, false, true, Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(), uint.MaxValue, ushort.MaxValue, 5, "ignored",
            new Dictionary<uint, uint>()).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)1);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0x04);
        await Assert.That(stream.ReadBoolean()).IsFalse();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task HarvestUpdated_WritesEveryVerifiedFieldIncludingErrorAndChildData()
    {
        var harvestData = new ButlerHarvestDataWire(uint.MaxValue, short.MaxValue, short.MinValue, uint.MaxValue,
            long.MinValue);
        var stream = new SCButlerHarvestUpdatedPacket(byte.MaxValue, short.MinValue, long.MaxValue, harvestData)
            .Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo(byte.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MinValue);
        await Assert.That(stream.ReadInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MinValue);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadInt64()).IsEqualTo(long.MinValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SpecialtyTradeUpdated_WritesEveryVerifiedField()
    {
        var data = new ButlerSpecialtyTradeDataWire(uint.MaxValue, ushort.MaxValue, ulong.MaxValue, int.MaxValue);
        var stream = new SCButlerSpecialtyTradeUpdatedPacket(byte.MaxValue, short.MinValue, long.MaxValue, data)
            .Write(new PacketStream());

        await Assert.That(stream.Count).IsEqualTo(29);
        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo(byte.MaxValue);
        await Assert.That(stream.ReadInt16()).IsEqualTo(short.MinValue);
        await Assert.That(stream.ReadInt64()).IsEqualTo(long.MaxValue);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(uint.MaxValue);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(ulong.MaxValue);
        await Assert.That(stream.ReadInt32()).IsEqualTo(int.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task HarvestUpdated_WritesZeroKindAndErrorInsteadOfOmittingThem()
    {
        var stream = new SCButlerHarvestUpdatedPacket(0, 0, 0, new ButlerHarvestDataWire(0, 0, 0, 0, 0))
            .Write(new PacketStream());

        await Assert.That(stream.Count).IsEqualTo(31);
        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0);
        await Assert.That(stream.ReadInt16()).IsEqualTo((short)0);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Unbound_WritesOnlyErrorMessage()
    {
        var stream = new SCButlerUnboundPacket(ushort.MaxValue).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(ushort.MaxValue);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ItemSwapped_WritesEchoTupleAndErrorInNativeOrder()
    {
        var stream = new SCButlerItemSwappedPacket(
            0x12, 0x34, 0x56, 0x78, 0x1122334455667788UL, 0x99AA).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x12);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x34);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x56);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x78);
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0x1122334455667788UL);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0x99AA);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ButlerOpcodes_MatchTheClientRegistrationSlots()
    {
        await Assert.That(SCOffsets.SCButlerInitInfoPacket).IsEqualTo((ushort)0x345);
        await Assert.That(SCOffsets.SCButlerBoundPacket).IsEqualTo((ushort)0x346);
        await Assert.That(SCOffsets.SCButlerUnboundPacket).IsEqualTo((ushort)0x348);
        await Assert.That(SCOffsets.SCButlerItemSwappedPacket).IsEqualTo((ushort)0x34A);
        await Assert.That(SCOffsets.SCButlerInfoUpdatedPacket).IsEqualTo((ushort)0x34B);
        await Assert.That(SCOffsets.SCButlerHarvestUpdatedPacket).IsEqualTo((ushort)0x34C);
        await Assert.That(SCOffsets.SCButlerSpawnedPacket).IsEqualTo((ushort)0x347);
        await Assert.That(SCOffsets.SCButlerDespawnedPacket).IsEqualTo((ushort)0x349);
        await Assert.That(SCOffsets.SCButlerLookChangedPacket).IsEqualTo((ushort)0x34D);
        await Assert.That(SCOffsets.SCButlerSpecialtyTradeUpdatedPacket).IsEqualTo((ushort)0x34E);
    }

    private static ButlerInfoWire PopulatedInfo() => new(
        ulong.MaxValue,
        sbyte.MinValue,
        "Farmhand 한",
        ushort.MaxValue,
        uint.MaxValue,
        0,
        ushort.MaxValue,
        new Dictionary<int, Item>
        {
            [0] = Item(1000, 10),
            [19] = Item(1019, 19),
            [33] = Item(1033, 33)
        },
        [Item(2000, 20)],
        new Dictionary<sbyte, ulong> { [3] = 30, [-1] = ulong.MaxValue },
        [new ButlerActabilityWire(9, uint.MaxValue, short.MinValue)],
        new Dictionary<long, ButlerHarvestDataWire>
        {
            [7] = new ButlerHarvestDataWire(70, short.MaxValue, short.MinValue, uint.MaxValue, long.MinValue),
            [-1] = new ButlerHarvestDataWire(10, 1, 2, 3, 4)
        },
        new Dictionary<long, ButlerSpecialtyTradeDataWire>
        {
            [8] = new ButlerSpecialtyTradeDataWire(80, ushort.MaxValue, ulong.MaxValue, int.MaxValue),
            [-2] = new ButlerSpecialtyTradeDataWire(20, 0, 3, int.MinValue)
        },
        new Dictionary<uint, uint> { [7] = uint.MaxValue, [3] = 4 });

    private static async Task AssertInfo(PacketStream stream, ButlerInfoWire expected)
    {
        await Assert.That(stream.ReadUInt64()).IsEqualTo(expected.OwnerId);
        await Assert.That(stream.ReadSByte()).IsEqualTo(expected.WorldId);
        await Assert.That(stream.ReadString()).IsEqualTo(expected.Name);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.HouseTlId);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(expected.LaborPower);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.LpChargedAmount);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(expected.RemainProductionCost);

        var expectedEquipmentFlags = expected.Equipment.Keys.Aggregate(0UL, (flags, slot) => flags | (1UL << slot));
        await Assert.That(stream.ReadUInt64()).IsEqualTo(expectedEquipmentFlags);
        foreach (var (slot, item) in expected.Equipment.OrderBy(entry => entry.Key))
        {
            if (slot is >= 19 and <= 25)
                await Assert.That(stream.ReadUInt32()).IsEqualTo(item.TemplateId);
            else
                await AssertItem(stream, item);
        }

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.BagItems.Count);
        foreach (var item in expected.BagItems)
            await AssertItem(stream, item);

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.PermanentDatas.Count);
        foreach (var (key, value) in expected.PermanentDatas.OrderBy(entry => entry.Key))
        {
            await Assert.That(stream.ReadSByte()).IsEqualTo(key);
            await Assert.That(stream.ReadUInt64()).IsEqualTo(value);
        }

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.Actabilities.Count);
        foreach (var actability in expected.Actabilities)
        {
            await Assert.That(stream.ReadPisc(2)).IsEquivalentTo(new[] { actability.GroupId, actability.Point });
            await Assert.That(stream.ReadInt16()).IsEqualTo(actability.Stat);
        }

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.HarvestDatas.Count);
        foreach (var (key, data) in expected.HarvestDatas.OrderBy(entry => entry.Key))
        {
            await Assert.That(stream.ReadInt64()).IsEqualTo(key);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(data.HarvestId);
            await Assert.That(stream.ReadInt16()).IsEqualTo(data.RepeatCount);
            await Assert.That(stream.ReadInt16()).IsEqualTo(data.RequestedAmount);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(data.LpForCalcExp);
            await Assert.That(stream.ReadInt64()).IsEqualTo(data.UpdateTime);
        }

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.SpecialtyTradeDatas.Count);
        foreach (var (key, data) in expected.SpecialtyTradeDatas.OrderBy(entry => entry.Key))
        {
            await Assert.That(stream.ReadInt64()).IsEqualTo(key);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(data.SpecialtyType);
            await Assert.That(stream.ReadUInt16()).IsEqualTo(data.ToZoneGroupType);
            await Assert.That(stream.ReadUInt64()).IsEqualTo(data.CreatedTime);
            await Assert.That(stream.ReadInt32()).IsEqualTo(data.DeliveryTime);
        }

        await Assert.That(stream.ReadInt32()).IsEqualTo(expected.UnitAttributes.Count);
        foreach (var (key, value) in expected.UnitAttributes.OrderBy(entry => entry.Key))
        {
            await Assert.That(stream.ReadUInt32()).IsEqualTo(key);
            await Assert.That(stream.ReadUInt32()).IsEqualTo(value);
        }
    }

    private static Item Item(uint templateId, ulong id) => new(1)
    {
        TemplateId = templateId,
        Id = id,
        Count = 1
    };

    private static async Task AssertItem(PacketStream stream, Item expected)
    {
        var actual = new Item(1);
        actual.Read(stream);

        await Assert.That(actual.TemplateId).IsEqualTo(expected.TemplateId);
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
    }
}
