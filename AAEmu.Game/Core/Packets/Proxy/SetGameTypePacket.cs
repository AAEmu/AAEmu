using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class SetGameTypePacket(string level, ulong checksum, byte immersive)
    : GamePacket(PPOffsets.SetGameTypePacket, 2)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(level);
        stream.Write(checksum);
        stream.Write(immersive);

        return stream;
    }
}
