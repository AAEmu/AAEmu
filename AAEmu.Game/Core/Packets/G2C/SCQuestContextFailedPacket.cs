using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Closes the accept / directing window and shows <c>QUEST_ERROR</c>.
/// Body is quest id then the 1-based reason from <see cref="QuestStatusFailed"/>.
/// </summary>
public class SCQuestContextFailedPacket(uint questId, QuestStatusFailed reason)
    : GamePacket(SCOffsets.SCQuestContextFailedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(questId);
        stream.Write((byte)reason);
        return stream;
    }
}
