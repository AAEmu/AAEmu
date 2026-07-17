using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResultRestrictCheckPacket(uint characterId, byte code, byte result)
    : GamePacket(SCOffsets.SCResultRestrictCheckPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(code);
        stream.Write(result);
        return stream;
    }
}
