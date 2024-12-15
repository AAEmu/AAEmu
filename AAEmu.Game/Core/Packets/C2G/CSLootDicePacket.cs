using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLootDicePacket : GamePacket
{
    public CSLootDicePacket() : base(CSOffsets.CSLootDicePacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var itemIndex = stream.ReadUInt16();
        var lootOwnerType = (LootOwnerType)stream.ReadUInt16();
        var lootOwnerObjId = stream.ReadBc();
        var b = stream.ReadByte();
        // var iid = stream.ReadUInt64();
        var roll = stream.ReadBoolean();
        
        Logger.Warn($"LootDice, ItemIndex: {itemIndex}, LootOwner: {lootOwnerType}:{lootOwnerObjId}, b: {b}, Roll: {roll}");
        
        // TODO: Validate lootOwner
        // TODO: Handle dice rolls
    }
}
