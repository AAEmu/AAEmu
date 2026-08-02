using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSTakedownTradeItemPacket() : GamePacket(CSOffsets.CSTakedownTradeItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        TradeManager.Instance.RemoveItem(character, slotType, slot);
    }
}
