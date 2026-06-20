using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDiceValuePacket(string name, int max, int value) : GamePacket(SCOffsets.SCDiceValuePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name); // TODO max length 48
        stream.Write(max);
        stream.Write(value);

        return stream;
    }
}
