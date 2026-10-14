using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The instance HUD readout's framing (0x2D8): the copy's zone instance id, then a nested buffer holding
/// <b>three</b> counted arrays — the <c>npcInfo</c> rows (buff id, npc id, broadcasting type, value), a 32-bit
/// gain-rule id list, then the
/// 64-bit <c>gainRuleInfo</c> list. The client only accepts the readout for the copy it is playing and reads
/// the arrays out of the nested buffer, so a plain array tail or a bare world id is discarded.
/// </summary>
public class SCIndunPlayingInfoBroadcastingPacketTests
{
    private const int ZoneInstanceIdSize = 8;

    [Test]
    public async Task Write_EmitsZoneInstanceIdThenNestedArrays()
    {
        var packet = new SCIndunPlayingInfoBroadcastingPacket(
            new ZoneInstanceId(280u, 100u),
            [new IndunPlayingInfoNpc(19307u, 621u, 2u, 30u, 60u)],
            [4u, 5u],
            [7ul, 9ul]);

        var stream = new PacketStream();
        packet.Write(stream);
        var bytes = stream.GetBytes();

        // zi = u32 zone key + u32 instance id
        await Assert.That(BitConverter.ToUInt32(bytes, 0)).IsEqualTo(280u);
        await Assert.That(BitConverter.ToUInt32(bytes, 4)).IsEqualTo(100u);

        const int payloadSize = 4 + 16 + 4 + 8 + 4 + 16;
        var header = ZoneInstanceIdSize;
        await Assert.That(BitConverter.ToUInt16(bytes, header)).IsEqualTo((ushort)(payloadSize + 4));
        await Assert.That(BitConverter.ToUInt16(bytes, header + 2)).IsEqualTo((ushort)(payloadSize + 4));
        await Assert.That(BitConverter.ToUInt16(bytes, header + 4)).IsEqualTo((ushort)(payloadSize + 2));
        await Assert.That(BitConverter.ToUInt16(bytes, header + 6)).IsEqualTo((ushort)0);

        var p = header + NestedBlobWire.HeaderSize;
        // u32 npcInfo count + 16-byte row: buff id (the client's key), npc id, broadcasting type, value.
        // The timer's whole duration is server-side only.
        await Assert.That(BitConverter.ToUInt32(bytes, p)).IsEqualTo(1u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 4)).IsEqualTo(621u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 8)).IsEqualTo(19307u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 12)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 16)).IsEqualTo(30u);
        // u32 gainRuleId count + u32 ids
        await Assert.That(BitConverter.ToUInt32(bytes, p + 20)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 24)).IsEqualTo(4u);
        await Assert.That(BitConverter.ToUInt32(bytes, p + 28)).IsEqualTo(5u);
        // u32 gainRuleInfo count + u64 keys
        await Assert.That(BitConverter.ToUInt32(bytes, p + 32)).IsEqualTo(2u);
        await Assert.That(BitConverter.ToUInt64(bytes, p + 36)).IsEqualTo(7ul);
        await Assert.That(BitConverter.ToUInt64(bytes, p + 44)).IsEqualTo(9ul);
        await Assert.That(bytes.Length).IsEqualTo(p + payloadSize);
    }

    [Test]
    public async Task Write_EmptyReadouts_StillCarriesAllThreeCounts()
    {
        var packet = new SCIndunPlayingInfoBroadcastingPacket(new ZoneInstanceId(280u, 100u), null, null, null);

        var stream = new PacketStream();
        packet.Write(stream);
        var bytes = stream.GetBytes();

        var p = ZoneInstanceIdSize + NestedBlobWire.HeaderSize;
        await Assert.That(bytes.Length).IsEqualTo(p + 12);
        await Assert.That(BitConverter.ToUInt16(bytes, ZoneInstanceIdSize + 4)).IsEqualTo((ushort)14);
        await Assert.That(BitConverter.ToUInt32(bytes, p)).IsEqualTo(0u);     // npcInfo
        await Assert.That(BitConverter.ToUInt32(bytes, p + 4)).IsEqualTo(0u); // gainRuleId
        await Assert.That(BitConverter.ToUInt32(bytes, p + 8)).IsEqualTo(0u); // gainRuleInfo
    }
}
