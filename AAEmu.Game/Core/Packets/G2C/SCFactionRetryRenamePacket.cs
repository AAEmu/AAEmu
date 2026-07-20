using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionRetryRenamePacket(bool xpdt, short errorMessage, string name)
    : GamePacket(SCOffsets.SCFactionRetryRenamePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(xpdt);
        stream.Write(errorMessage);
        stream.Write(name);
        return stream;
    }
}
