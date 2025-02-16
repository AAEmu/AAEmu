using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCQuestContextResetPacket : GamePacket
{
    private readonly uint _questId;
    private readonly byte[] _body;
    private readonly uint _componentId;

    public SCQuestContextResetPacket(uint questId, byte[] body, uint componentId) : base(SCOffsets.SCQuestContextResetPacket, 5)
    {
        _questId = questId;
        _body = body;
        _componentId = componentId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_questId);
        //stream.Write(_body); // ulong -> byte[8] // not in version 5070
        //stream.Write(_componentId); // not in version 5070
        return stream;
    }
}
