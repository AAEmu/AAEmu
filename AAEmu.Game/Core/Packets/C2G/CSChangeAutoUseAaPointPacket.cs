using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeAutoUseAaPointPacket : GamePacket
{
    public CSChangeAutoUseAaPointPacket() : base(CSOffsets.CSChangeAutoUseAaPointPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSChangeAutoUseAaPointPacket");
    }
}
