using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJoinSquadMemberPacket(
    ulong worldCharKey,
    string charName,
    byte level,
    byte ability1,
    byte ability2,
    byte ability3,
    int eloRating)
    : GamePacket(SCOffsets.SCJoinSquadMemberPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldCharKey);
        stream.Write(charName);
        stream.Write(level);
        stream.Write(ability1);
        stream.Write(ability2);
        stream.Write(ability3);
        stream.Write(eloRating);
        return stream;
    }
}
