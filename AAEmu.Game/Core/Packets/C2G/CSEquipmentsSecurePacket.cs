using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSEquipmentsSecurePacket() : GamePacket(CSOffsets.CSEquipmentsSecurePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        Logger.Warn("EquipmentsSecure");
    }
}
