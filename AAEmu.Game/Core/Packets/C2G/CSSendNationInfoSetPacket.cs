using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendNationInfoSetPacket : GamePacket
{
    public CSSendNationInfoSetPacket() : base(CSOffsets.CSSendNationInfoSetPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSSendNationInfoSetPacket");
    }
}
