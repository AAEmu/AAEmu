using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDominionDataPacket(DominionData dominionData, bool newlyDeclared, bool finalDataByRequest)
    : GamePacket(SCOffsets.SCDominionDataPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(dominionData);
        stream.Write(newlyDeclared);
        stream.Write(finalDataByRequest);
        return stream;
    }
}
