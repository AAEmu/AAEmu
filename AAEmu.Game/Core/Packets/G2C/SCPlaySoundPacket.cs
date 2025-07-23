using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlaySoundPacket : GamePacket
{
    private readonly byte _kind;
    private readonly uint _bubbleId;
    private readonly uint _npcObjId;

    public SCPlaySoundPacket(byte kind, uint bubbleId, uint npcObjId) : base(SCOffsets.SCPlaySoundPacket, 1)
    {
        _kind = kind;
        _bubbleId = bubbleId;
        _npcObjId = npcObjId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_kind);
        stream.Write(_bubbleId);
        stream.Write(_npcObjId);

        return stream;
    }
}
