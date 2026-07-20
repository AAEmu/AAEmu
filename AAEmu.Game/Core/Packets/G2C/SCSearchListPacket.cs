using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSearchListPacket(int total, Friend[] friends, bool success) : GamePacket(SCOffsets.SCSearchListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(total);
        stream.Write(friends.Length); // TODO max length 200
        foreach (var friend in friends)
            stream.Write(friend);
        stream.Write(success);
        return stream;
    }
}
