using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCriminalArrestedPacket(uint criminalObjId, string criminal, string arrestor) : GamePacket(SCOffsets.SCCriminalArrestedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(criminalObjId);
        stream.Write(criminal);
        stream.Write(arrestor);
        return stream;
    }
}
