using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSPutupTradeMoneyPacket() : GamePacket(CSOffsets.CSPutupTradeMoneyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var moneyAmount = stream.ReadInt64();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        TradeManager.Instance.AddMoney(character, moneyAmount);
    }
}
