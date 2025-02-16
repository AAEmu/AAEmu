using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJurySentencePacket : GamePacket
{
    public CSJurySentencePacket() : base(CSOffsets.CSJurySentencePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSJurySentencePacket");
    }
}
