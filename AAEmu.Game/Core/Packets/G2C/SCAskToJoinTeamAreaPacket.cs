using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAskToJoinTeamAreaPacket(uint teamId, uint senderId, string senderName, bool isParty)
    : GamePacket(SCOffsets.SCAskToJoinTeamAreaPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(senderId);
        stream.Write(senderName);
        stream.Write(isParty);
        return stream;
    }
}
