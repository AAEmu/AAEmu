using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCDoodadQuestPacketTests
{
    [Test]
    public async Task AcceptAndComplete_ShareBcThenQuestId()
    {
        var accept = new SCDoodadQuestAcceptPacket(189512, 2388).Write(new PacketStream()).GetBytes();
        var complete = new SCDoodadCompleteQuestPacket(189512, 2387).Write(new PacketStream()).GetBytes();
        var expectedAccept = new PacketStream();
        expectedAccept.WriteBc(189512);
        expectedAccept.Write(2388u);
        var expectedComplete = new PacketStream();
        expectedComplete.WriteBc(189512);
        expectedComplete.Write(2387u);

        await Assert.That(Hex.Of(accept)).IsEqualTo(Hex.Of(expectedAccept.GetBytes()));
        await Assert.That(Hex.Of(complete)).IsEqualTo(Hex.Of(expectedComplete.GetBytes()));
        await Assert.That(SCOffsets.SCDoodadQuestAcceptPacket).IsEqualTo((ushort)0x153);
        await Assert.That(SCOffsets.SCDoodadCompleteQuestPacket).IsEqualTo((ushort)0x193);
    }
}
