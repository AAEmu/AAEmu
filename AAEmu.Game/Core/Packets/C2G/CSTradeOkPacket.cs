using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.SensitiveOperation;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTradeOkPacket() : GamePacket(CSOffsets.CSTradeOkPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // Confirming is the point a trade becomes irreversible, so it is where an account-protection window
        // holds it back. Inert unless feature bit 56 is on.
        if (!SensitiveOperationGuard.MayPerform(character, SensitiveOperationKind.Trade, out var reason))
        {
            character.SendMessage(ChatType.System, reason);
            return;
        }

        TradeManager.Instance.OkTrade(character);
    }
}
