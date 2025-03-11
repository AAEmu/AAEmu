using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendRelationVotePacket : GamePacket
{
    public CSSendRelationVotePacket() : base(CSOffsets.CSSendRelationVotePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSSendRelationVotePacket");
    }
}
