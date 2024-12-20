using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Player does a die roll response
/// </summary>
public class CSLootDicePacket() : GamePacket(CSOffsets.CSLootDicePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var itemIndex = stream.ReadUInt16();
        var lootOwnerType = (LootOwnerType)stream.ReadUInt16();
        var lootOwnerObjId = stream.ReadBc();
        var b = stream.ReadByte();
        var rollRequest = stream.ReadBoolean(); // this might be a byte instead?

        byte[] remainingBytes = [];
        if (stream.Pos < stream.Count)
        {
            remainingBytes = stream.ReadBytes(stream.Count - stream.Pos);
        }
        
        Logger.Warn($"CSLootDice, ItemIndex: {itemIndex}, LootOwner: {lootOwnerType}:{lootOwnerObjId}, b: {b}, Roll: {rollRequest}, remainingBytes: {remainingBytes.Length}");

        BaseUnit lootOwner = null;
        switch (lootOwnerType)
        {
            case LootOwnerType.Npc:
                lootOwner = WorldManager.Instance.GetNpc(lootOwnerObjId);
                break;
            case LootOwnerType.Doodad:
                lootOwner = WorldManager.Instance.GetDoodad(lootOwnerObjId);
                break;
        }

        if (lootOwner == null)
        {
            Logger.Warn($"CSLootDice, LootOwner not found: {lootOwnerType}:{lootOwnerObjId} by {Connection.ActiveChar.Name}");
            return;
        }
        lootOwner.LootingContainer.DoPlayerRoll(Connection.ActiveChar, itemIndex, rollRequest);
    }
}
