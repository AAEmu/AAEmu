using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendNationMemberCountListPacket : GamePacket
{
    public CSSendNationMemberCountListPacket() : base(CSOffsets.CSSendNationMemberCountListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSSendNationMemberCountListPacket");
    }
}
