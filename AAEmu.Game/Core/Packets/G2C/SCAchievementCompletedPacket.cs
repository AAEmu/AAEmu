using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAchievementCompletedPacket(uint id) : GamePacket(SCOffsets.SCAchievementCompletedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);             // type
        stream.Write(DateTime.UtcNow); // complete

        return stream;
    }
}
