using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamAreaInvitedPacket(int remaining, bool success) : GamePacket(SCOffsets.SCTeamAreaInvitedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(remaining);
        stream.Write(success);
        return stream;
    }
}
