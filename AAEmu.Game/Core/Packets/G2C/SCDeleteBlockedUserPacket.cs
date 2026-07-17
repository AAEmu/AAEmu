using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDeleteBlockedUserPacket(uint characterId, bool success, string blockedName, short errorMessage)
    : GamePacket(SCOffsets.SCDeleteBlockedUserPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(success);
        stream.Write(blockedName);
        stream.Write(errorMessage);
        return stream;
    }
}
