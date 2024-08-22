using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCheckDemoModePacket : GamePacket
{
    public CSCheckDemoModePacket() : base(CSOffsets.CSCheckDemoModePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // no body
        Logger.Warn("CSCheckDemoModePacket");
    }
}
