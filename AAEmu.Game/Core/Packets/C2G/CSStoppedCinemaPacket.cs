using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStoppedCinemaPacket : GamePacket
{
    public CSStoppedCinemaPacket() : base(CSOffsets.CSStoppedCinemaPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        // Empty struct
        Logger.Warn("CSStoppedCinemaPacket");
    }
}
