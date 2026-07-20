using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBlockedUsersPacket(int total, Blocked[] blocked) : GamePacket(SCOffsets.SCBlockedUsersPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write(blocked.Length); // TODO max length 500
        foreach (var blocked1 in blocked)
            stream.Write(blocked1);
        return stream;
    }
}
