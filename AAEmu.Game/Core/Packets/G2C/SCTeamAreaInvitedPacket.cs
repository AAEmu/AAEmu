using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamAreaInvitedPacket(uint r, bool s) : GamePacket(SCOffsets.SCTeamAreaInvitedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(r);
        stream.Write(s);
        return stream;
    }
}
