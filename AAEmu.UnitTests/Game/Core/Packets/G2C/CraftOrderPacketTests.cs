using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Pins the craft-order wire: the entry layout the client reads, the caps it substitutes for an
/// over-long count, and the bodies of the five client requests.
/// </summary>
public class CraftOrderPacketTests
{
    private const ulong EntryId = 0x0102030405060708;
    private const ulong OtherId = 0x1112131415161718;

    private static CraftOrderEntry Entry(ulong id = EntryId) => new(
        Id: id,
        Kind: 2,
        Unnamed1: 0x2122232425262728,
        OrderItemId: 0x3132333435363738,
        Unnamed2: 0x41424344,
        CraftCount: 7,
        Unnamed3: 5,
        MoneyAmount: 0x5152535455565758,
        Unnamed4: 0x61626364,
        ActabilityPoint: 120_000,
        PostDate: 1_700_000_000,
        ExpireDate: 1_700_172_800,
        Status: 1,
        Unnamed5: 0x7172737475767778);

    private static CraftOrderMaterialRow Material(uint type = 0x81828384, byte type2 = 3, uint stack = 9) =>
        new(type, type2, stack);

    [Test]
    public async Task Entry_OccupiesSeventyFiveBytesInTheOrderTheClientReadsThem()
    {
        var body = CraftOrderWire.WriteEntry(new PacketStream(), Entry()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(CraftOrderWire.EntrySize);

        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(EntryId);              // id
        await Assert.That(body[8]).IsEqualTo((byte)2);                                     // kind
        await Assert.That(BitConverter.ToUInt64(body, 9)).IsEqualTo(0x2122232425262728ul);
        await Assert.That(BitConverter.ToUInt64(body, 17)).IsEqualTo(0x3132333435363738ul); // orderItemId
        await Assert.That(BitConverter.ToUInt32(body, 25)).IsEqualTo(0x41424344u);
        await Assert.That(BitConverter.ToUInt32(body, 29)).IsEqualTo(7u);                   // craftCount
        await Assert.That(body[33]).IsEqualTo((byte)5);
        await Assert.That(BitConverter.ToUInt64(body, 34)).IsEqualTo(0x5152535455565758ul); // moneyAmount
        await Assert.That(BitConverter.ToUInt32(body, 42)).IsEqualTo(0x61626364u);
        await Assert.That(BitConverter.ToUInt32(body, 46)).IsEqualTo(120_000u);             // actabilityPoint
        await Assert.That(BitConverter.ToUInt64(body, 50)).IsEqualTo(1_700_000_000ul);      // postDate
        await Assert.That(BitConverter.ToUInt64(body, 58)).IsEqualTo(1_700_172_800ul);      // expireDate
        await Assert.That(body[66]).IsEqualTo((byte)1);                                     // status
        await Assert.That(BitConverter.ToUInt64(body, 67)).IsEqualTo(0x7172737475767778ul);
    }

    [Test]
    public async Task Load_WritesTheCountThenEveryEntry()
    {
        var body = new SCLoadCraftOrderEntryPacket([Entry(), Entry(OtherId)])
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(4 + (2 * CraftOrderWire.EntrySize));
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt64(body, 4)).IsEqualTo(EntryId);
        await Assert.That(BitConverter.ToUInt64(body, 4 + CraftOrderWire.EntrySize)).IsEqualTo(OtherId);
    }

    [Test]
    public async Task Load_WritesAnEmptyListAsAZeroCount()
    {
        var body = new SCLoadCraftOrderEntryPacket([]).Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(4);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(0u);
    }

    [Test]
    public async Task Load_RefusesMoreEntriesThanTheClientKeeps()
    {
        // The client substitutes its own cap for an over-long count, so a truncated list would decode
        // as someone else's orders; refuse instead of sending it.
        var entries = Enumerable.Range(0, CraftOrderWire.LoadEntryLimit + 1)
            .Select(i => Entry((ulong)i))
            .ToList();

        await Assert.That(() => new SCLoadCraftOrderEntryPacket(entries).Write(new PacketStream()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InsertAndComplete_CarryOneEntryAndNoCount()
    {
        var insert = new SCInsertCraftOrderEntryPacket(Entry()).Write(new PacketStream()).GetBytes();
        var complete = new SCCompleteCraftOrderEntryPacket(Entry()).Write(new PacketStream()).GetBytes();

        await Assert.That(insert.Length).IsEqualTo(CraftOrderWire.EntrySize);
        await Assert.That(complete.Length).IsEqualTo(CraftOrderWire.EntrySize);
        await Assert.That(BitConverter.ToUInt64(insert, 0)).IsEqualTo(EntryId);
        await Assert.That(BitConverter.ToUInt64(complete, 0)).IsEqualTo(EntryId);
    }

    [Test]
    public async Task Searched_WritesTotalPageCountThenThePage()
    {
        var body = new SCCraftOrderEntrySearchedPacket(totalCount: 40, page: 3, [Entry()])
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(40u);   // totalCount, not the row count
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(3u);    // page
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(1u);    // count
        await Assert.That(body.Length).IsEqualTo(12 + CraftOrderWire.EntrySize);
    }

    [Test]
    public async Task Searched_RefusesMoreEntriesThanOnePageHolds()
    {
        var entries = Enumerable.Range(0, CraftOrderWire.SearchEntryLimit + 1)
            .Select(i => Entry((ulong)i))
            .ToList();

        await Assert.That(() => new SCCraftOrderEntrySearchedPacket(9, 0, entries).Write(new PacketStream()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Items_WritesTheItemThenCountPrefixedNineByteRows()
    {
        var body = new SCCraftOrderItemsPacket(0x3132333435363738, [Material(), Material(stack: 12)])
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x3132333435363738ul);
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt32(body, 12)).IsEqualTo(0x81828384u);
        await Assert.That(body[16]).IsEqualTo((byte)3);
        await Assert.That(BitConverter.ToUInt32(body, 17)).IsEqualTo(9u);
        await Assert.That(BitConverter.ToUInt32(body, 12 + CraftOrderWire.MaterialRowSize)).IsEqualTo(0x81828384u);
        await Assert.That(body.Length).IsEqualTo(12 + (2 * CraftOrderWire.MaterialRowSize));
    }

    [Test]
    public async Task Items_RefusesMoreMaterialRowsThanTheClientShows()
    {
        var rows = Enumerable.Range(0, CraftOrderWire.MaterialRowLimit + 1).Select(_ => Material()).ToList();

        await Assert.That(() => new SCCraftOrderItemsPacket(1, rows).Write(new PacketStream()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ActionResult_IsExactlyTheKindByteAndTheResultFlag()
    {
        var body = new SCCraftOrderActionResultPacket(kind: 4, result: true).Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(2);
        await Assert.That(body[0]).IsEqualTo((byte)4);
        await Assert.That(body[1]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task Delete_CarriesTheOrderIdThenTheCompletionFlag()
    {
        var body = new SCDeleteCraftOrderEntryPacket(EntryId, complete: true).Write(new PacketStream()).GetBytes();

        await Assert.That(body.Length).IsEqualTo(9);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(EntryId);
        await Assert.That(body[8]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task FeeInfo_IsTypeThenTwoAmountsThenTheResultFlag()
    {
        var body = new SCCraftOrderFeeInfoPacket(type: 6, moneyAmount: 1_000, moneyAmount2: 250, result: false)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(21);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(6u);
        await Assert.That(BitConverter.ToUInt64(body, 4)).IsEqualTo(1_000ul);
        await Assert.That(BitConverter.ToUInt64(body, 12)).IsEqualTo(250ul);
        await Assert.That(body[20]).IsEqualTo((byte)0);
    }

    [Test]
    public async Task Search_ParsesTheElevenByteRequest()
    {
        var body = new PacketStream().Write(4).Write((sbyte)1).Write((sbyte)2).Write(3u).Write(true).GetBytes();
        await Assert.That(body.Length).IsEqualTo(11);

        var packet = new CSSearchCraftOrderPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.Type).IsEqualTo(4);
        await Assert.That(packet.Kind).IsEqualTo((sbyte)1);
        await Assert.That(packet.Order).IsEqualTo((sbyte)2);
        await Assert.That(packet.Page).IsEqualTo(3u);
        await Assert.That(packet.Possible).IsTrue();
    }

    [Test]
    public async Task Post_ParsesTheSixteenByteRequest()
    {
        var body = new PacketStream().Write((long)0x3132333435363738).Write(0x5152535455565758ul).GetBytes();
        await Assert.That(body.Length).IsEqualTo(16);

        var packet = new CSPostCraftOrderPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.ItemId).IsEqualTo(0x3132333435363738);
        await Assert.That(packet.MoneyAmount).IsEqualTo(0x5152535455565758ul);
    }

    [Test]
    public async Task Cancel_ParsesTheEightByteRequest()
    {
        var body = new PacketStream().Write(EntryId).GetBytes();
        await Assert.That(body.Length).IsEqualTo(8);

        var packet = new CSCancelCraftOrderPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.EntryId).IsEqualTo(EntryId);
    }

    [Test]
    public async Task Fee_ParsesTheFourByteRequest()
    {
        var body = new PacketStream().Write(11).GetBytes();
        await Assert.That(body.Length).IsEqualTo(4);

        var packet = new CSRequestCraftOrderFeePacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.TypeValue).IsEqualTo(11);
    }

    [Test]
    public async Task ItemsRequest_ParsesTheEightByteRequest()
    {
        var body = new PacketStream().Write((long)0x3132333435363738).GetBytes();
        await Assert.That(body.Length).IsEqualTo(8);

        var packet = new CSRequestCraftOrderItemsPacket();
        packet.Read(new PacketStream(body));

        await Assert.That(packet.ItemId).IsEqualTo(0x3132333435363738);
    }

    [Test]
    public async Task CraftOrderFamily_OpcodesMatchThePinnedTable()
    {
        await Assert.That((int)CSOffsets.CSSearchCraftOrderPacket).IsEqualTo(0x146);
        await Assert.That((int)CSOffsets.CSPostCraftOrderPacket).IsEqualTo(0x147);
        await Assert.That((int)CSOffsets.CSCancelCraftOrderPacket).IsEqualTo(0x148);
        await Assert.That((int)CSOffsets.CSRequestCraftOrderFeePacket).IsEqualTo(0x149);
        await Assert.That((int)CSOffsets.CSRequestCraftOrderItemsPacket).IsEqualTo(0x14A);

        await Assert.That((int)SCOffsets.SCLoadCraftOrderEntryPacket).IsEqualTo(0x231);
        await Assert.That((int)SCOffsets.SCInsertCraftOrderEntryPacket).IsEqualTo(0x232);
        await Assert.That((int)SCOffsets.SCDeleteCraftOrderEntryPacket).IsEqualTo(0x233);
        await Assert.That((int)SCOffsets.SCCompleteCraftOrderEntryPacket).IsEqualTo(0x234);
        await Assert.That((int)SCOffsets.SCCraftOrderActionResultPacket).IsEqualTo(0x235);
        await Assert.That((int)SCOffsets.SCCraftOrderFeeInfoPacket).IsEqualTo(0x236);
        await Assert.That((int)SCOffsets.SCCraftOrderItemsPacket).IsEqualTo(0x237);
        await Assert.That((int)SCOffsets.SCCraftOrderEntrySearchedPacket).IsEqualTo(0x238);
    }
}
