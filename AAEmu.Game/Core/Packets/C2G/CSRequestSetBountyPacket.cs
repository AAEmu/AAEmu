using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestSetBountyPacket : GamePacket
{
    public CSRequestSetBountyPacket() : base(CSOffsets.CSRequestSetBountyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSRequestSetBountyPacket");
    }
}
