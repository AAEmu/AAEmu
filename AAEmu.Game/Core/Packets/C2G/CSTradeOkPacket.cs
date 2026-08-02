using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTradeOkPacket() : GamePacket(CSOffsets.CSTradeOkPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character != null)
            TradeManager.Instance.OkTrade(character);
    }
}
