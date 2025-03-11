using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDemoCharResetPacket : GamePacket
{
    public CSDemoCharResetPacket() : base(CSOffsets.CSDemoCharResetPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSDemoCharResetPacket");
    }
}
