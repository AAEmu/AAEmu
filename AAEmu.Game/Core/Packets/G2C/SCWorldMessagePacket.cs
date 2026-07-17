using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldMessagePacket(byte source, byte messageType, string msg)
    : GamePacket(SCOffsets.SCWorldMessagePacket, 5)
{
    // SEENS RELATED TO PATRON TIME TO EXPIRE

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(source);
        stream.Write(messageType);
        stream.Write(msg);
        return stream;
    }
}
