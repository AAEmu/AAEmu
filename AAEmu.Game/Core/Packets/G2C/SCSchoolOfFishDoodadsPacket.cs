using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSchoolOfFishDoodadsPacket(bool last, Doodad[] transfers)
    : GamePacket(SCOffsets.SCSchoolOfFishDoodadsPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)transfers.Length); // не более 10
        foreach (var transfer in transfers)
        {
            transfer.WriteFishFinderUnit(stream);
        }

        return stream;
    }
}
