using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootCloseBagPacket : GamePacket
{
    public CSLootCloseBagPacket() : base(CSOffsets.CSLootCloseBagPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var itemIndex = stream.ReadUInt16();
        var ownerType = (LootOwnerType)stream.ReadUInt16();
        var ownerObjId = stream.ReadBc();
        var b = stream.ReadByte();
        // var iid = stream.ReadUInt64();

        Logger.Warn($"LootCloseBag, itemIndex: {itemIndex}, LootOwner: {ownerType}:{ownerObjId}, b: {b}");
    }
}
