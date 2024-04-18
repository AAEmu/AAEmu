using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSThisTimeUnpackItemPacket : GamePacket
{
    public CSThisTimeUnpackItemPacket() : base(CSOffsets.CSThisTimeUnpackPacket, 5)
    {

    }

    public override void Read(PacketStream stream)
    {
        var slotType = (SlotType)stream.ReadByte();
        var slot = stream.ReadByte();
        var itemId = stream.ReadUInt64();

        Logger.Debug($"CSThisTimeUnpackItemPacket: slotType: {slotType}, slot: {slot}, itemId: {itemId}");
        if (!ItemManager.Instance.UnwrapItem(Connection.ActiveChar, slotType, slot, itemId))
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ItemUpdateFail);
    }
}
