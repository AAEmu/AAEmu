using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCQuestContextFailedPacketTests
{
    [Test]
    public async Task Body_IsQuestIdThenReasonByte()
    {
        var body = new SCQuestContextFailedPacket(4781, QuestStatusFailed.UnitRequirementCheck)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(body.Length).IsEqualTo(5);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(4781u);
        await Assert.That(body[4]).IsEqualTo((byte)QuestStatusFailed.UnitRequirementCheck);
    }

    [Test]
    public async Task AlreadyHaveAndCompleted_KeepTheirTableSlots()
    {
        var have = new SCQuestContextFailedPacket(1, QuestStatusFailed.AlreadyHave)
            .Write(new PacketStream())
            .GetBytes();
        var done = new SCQuestContextFailedPacket(1, QuestStatusFailed.AlreadyCompleted)
            .Write(new PacketStream())
            .GetBytes();

        await Assert.That(have[4]).IsEqualTo((byte)1);
        await Assert.That(done[4]).IsEqualTo((byte)27);
    }
}
