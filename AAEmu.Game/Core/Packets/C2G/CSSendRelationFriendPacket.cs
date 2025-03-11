using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendRelationFriendPacket : GamePacket
{
    public CSSendRelationFriendPacket() : base(CSOffsets.CSSendRelationFriendPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSSendRelationFriendPacket");
    }
}
