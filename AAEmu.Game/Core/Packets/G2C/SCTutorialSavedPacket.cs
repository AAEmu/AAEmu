using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTutorialSavedPacket(uint id, byte[] body) : GamePacket(SCOffsets.SCTutorialSavedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(body);
        return stream;
    }
}
