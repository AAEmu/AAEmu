using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAchievementResetedPacket(uint id, int amount) : GamePacket(SCOffsets.SCAchievementResetedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);     // type
        stream.Write(amount); // amount
        stream.Write(DateTime.UtcNow); // complete

        return stream;
    }
}
