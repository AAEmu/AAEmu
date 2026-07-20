using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTransferTelescopeUnitsPacket(bool last, Transfer[] transfers)
    : GamePacket(SCOffsets.SCTransferTelescopeUnitsPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)transfers.Length);
        foreach (var transfer in transfers)
        {
            transfer.WriteTelescopeUnit(stream);
        }

        return stream;
    }
}
