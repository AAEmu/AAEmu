using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTelescopeToggledPacket(bool on, float range) : GamePacket(SCOffsets.SCTelescopeToggledPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(on);
        stream.Write(range);
        return stream;
    }
}
