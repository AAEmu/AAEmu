using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharBriefPacket(uint charId, string name, Race race) : GamePacket(SCOffsets.SCCharBriefPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charId);
        stream.Write(name);
        stream.Write((byte)race);
        return stream;
    }
}
