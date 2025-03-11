using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextStartedPacket : GamePacket
{
    private Quest _quest;
    private uint _componentId;

    public SCQuestContextStartedPacket(Quest quest, uint componentId) : base(SCOffsets.SCQuestContextStartedPacket, 5)
    {
        _quest = quest;
        _componentId = componentId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        _quest.Write(stream);
        stream.Write(_componentId);
        return stream;
    }
}
