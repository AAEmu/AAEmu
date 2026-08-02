using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// and <c>i32 amount</c>.
/// </remarks>
public class CSPutupTradeItemPacket() : GamePacket(CSOffsets.CSPutupTradeItemPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var amount = stream.ReadInt32();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        TradeManager.Instance.AddItem(character, slotType, slot, amount);
    }
}
