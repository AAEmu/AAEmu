using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRejectedTeamPacket(string name, bool party) : GamePacket(SCOffsets.SCRejectedTeamPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write(party);
        return stream;
    }
}
