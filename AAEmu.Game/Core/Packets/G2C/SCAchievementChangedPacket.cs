using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAchievementChangedPacket(uint id, int amount) : GamePacket(SCOffsets.SCAchievementChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);     // type
        stream.Write(amount); // amount

        return stream;
    }
}
