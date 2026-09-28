using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCCompletedQuestsPacketTests
{
    [Test]
    public async Task FieldOfHonor_IsBlock37Bit17()
    {
        const uint questId = 2385;
        var block = new CompletedQuest((ushort)(questId / 64));
        block.Body.Set((int)(questId % 64), true);

        var bytes = new SCCompletedQuestsPacket([block]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(1);
        expected.Write((uint)block.Id);
        var body = new byte[8];
        block.Body.CopyTo(body, 0);
        expected.Write(body);

        await Assert.That(block.Id).IsEqualTo((ushort)37);
        await Assert.That(block.Body.Get(17)).IsTrue();
        await Assert.That(BitConverter.ToUInt64(body, 0) & (1UL << 17)).IsEqualTo(1UL << 17);
        await Assert.That(Hex.Of(bytes)).IsEqualTo(Hex.Of(expected.GetBytes()));
        await Assert.That(SCOffsets.SCCompletedQuestsPacket).IsEqualTo((ushort)0x133);
    }

    [Test]
    public async Task EmptyList_WritesCountZero()
    {
        var bytes = new SCCompletedQuestsPacket([]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(0);

        await Assert.That(Hex.Of(bytes)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task DirtyBlock_CarriesTournamentCompleteBits()
    {
        // Same 64-bit block as login SendCompleted; turn-in should push this delta.
        const uint q2385 = 2385;
        const uint q2388 = 2388;
        var block = new CompletedQuest((ushort)(q2385 / 64));
        block.Body.Set((int)(q2385 % 64), true);
        block.Body.Set((int)(q2388 % 64), true);

        var bytes = new SCCompletedQuestsPacket([block]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(1);
        expected.Write((uint)block.Id);
        var body = new byte[8];
        block.Body.CopyTo(body, 0);
        expected.Write(body);

        await Assert.That(block.Id).IsEqualTo((ushort)37);
        await Assert.That(block.Body.Get((int)(q2388 % 64))).IsTrue();
        await Assert.That(Hex.Of(bytes)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }
}
