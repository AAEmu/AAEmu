using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelTradePacket() : GamePacket(CSOffsets.CSCancelTradePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var reason = stream.ReadInt32();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        TradeManager.Instance.CancelTrade(character.ObjId, reason);
    }
}
