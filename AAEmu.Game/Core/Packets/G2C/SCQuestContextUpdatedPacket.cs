using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextUpdatedPacket : GamePacket
{
    private readonly Quest _quest;
    private readonly uint _componentId;
    private readonly int _para1;
    private readonly int _para2;
    private readonly int _para3;
    private readonly int _para4;

    public SCQuestContextUpdatedPacket(Quest quest, uint componentId, int para1 = 0, int para2 = 0, int para3 = 0, int para4 = 0)
        : base(SCOffsets.SCQuestContextUpdatedPacket, 5)
    {
        _quest = quest;
        _componentId = componentId;
        _para1 = para1;
        _para2 = para2;
        _para3 = para3;
        _para4 = para4;
    }

    public override PacketStream Write(PacketStream stream)
    {
        _quest.Write(stream);
        stream.WritePiscW(5, [_componentId, _para1, _para2, _para3, _para4]);

        return stream;
    }
}
