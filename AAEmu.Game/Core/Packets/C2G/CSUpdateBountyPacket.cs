using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUpdateBountyPacket : GamePacket
{
    public CSUpdateBountyPacket() : base(CSOffsets.CSUpdateBountyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSUpdateBountyPacket");
    }
}
