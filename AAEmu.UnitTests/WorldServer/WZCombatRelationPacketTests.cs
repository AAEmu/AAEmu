using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.World.Core.Packets.Wz;

namespace AAEmu.UnitTests.WorldServer;

public class WZCombatRelationPacketTests
{
    [Test]
    public async Task CvF_WritesCountAndFourUInt32Fields()
    {
        var entries = new[]
        {
            new CombatRelationEntry(11, 22, 0, 0xA1B2C3D4),
            new CombatRelationEntry(33, 44, 7, 0x01020304),
        };

        var frame = new PacketStream(new WZCvFCombatRelationshipPacket(entries).Encode());

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)(2 + 1 + (2 * WZCombatRelationPacket.EntrySize)));
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.CvFCombatRelationship);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)entries.Length);
        foreach (var entry in entries)
        {
            await Assert.That(frame.ReadUInt32()).IsEqualTo(entry.Faction1);
            await Assert.That(frame.ReadUInt32()).IsEqualTo(entry.Faction2);
            await Assert.That(frame.ReadUInt32()).IsEqualTo(entry.RelationType);
            await Assert.That(frame.ReadUInt32()).IsEqualTo(entry.Flags);
        }
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public async Task FvF_WritesTheSameBodyWithItsOpcode()
    {
        var entries = new[] { new CombatRelationEntry(101, 202, 3, 9) };
        var frame = new PacketStream(new WZFvFCombatRelationshipPacket(entries).Encode());

        await Assert.That(frame.ReadUInt16()).IsEqualTo((ushort)(2 + 1 + WZCombatRelationPacket.EntrySize));
        await Assert.That(frame.ReadUInt16()).IsEqualTo(WzOpcodes.FvFCombatRelationship);
        await Assert.That(frame.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(101u);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(202u);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(3u);
        await Assert.That(frame.ReadUInt32()).IsEqualTo(9u);
        await Assert.That(frame.Pos).IsEqualTo(frame.Count);
    }

    [Test]
    public void Constructor_RejectsMoreThanOneByteCount()
    {
        var entries = Enumerable.Repeat(new CombatRelationEntry(1, 2, 3, 4), WZCombatRelationPacket.MaxEntriesPerPacket + 1).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => new WZCvFCombatRelationshipPacket(entries));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WZFvFCombatRelationshipPacket(entries));
    }

    [Test]
    public void Decode_RejectsTruncatedRecord()
    {
        var body = new PacketStream();
        body.Write((byte)1);
        body.Write(1u);
        body.Write(2u);
        body.Write(3u);
        body.Write(4u);
        var truncated = new PacketStream(body.GetBytes()[..^2]);

        Assert.Throws<InvalidDataException>(() => WZCombatRelationPacket.Decode(truncated));
    }

    [Test]
    public void Decode_RejectsTrailingBytes()
    {
        var body = new PacketStream();
        body.Write((byte)0);
        body.Write((byte)0xFF);

        Assert.Throws<InvalidDataException>(() => WZCombatRelationPacket.Decode(body));
    }
}
