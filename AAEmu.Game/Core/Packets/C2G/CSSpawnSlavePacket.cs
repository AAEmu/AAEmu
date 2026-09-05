using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpawnSlavePacket() : GamePacket(CSOffsets.CSSpawnSlavePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slaveId = stream.ReadUInt32();
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var zRot = stream.ReadSingle();
        var itemId = stream.ReadUInt64();

        // slot pair is two bytes and is only on the wire when item != 0. The packet class
        // zeroes +0x40/+0x42 locally; only +0x41 (slotType) and +0x43 (slot) are read.
        var slotType = SlotType.None;
        byte slot = 0;
        if (itemId != 0)
        {
            slotType = (SlotType)stream.ReadByte();
            slot = stream.ReadByte();
        }

        // client off slaves.portal_spawn_fx_id in its own data, so there is nothing server-side to
        // suppress; the byte is still consumed here to keep the read aligned with the wire.
        var hideSpawnEffect = stream.ReadBoolean();

        Logger.Debug(
            "SpawnSlave, SlaveId: {0}, item: {1}, slot: {2}/{3}, pos: ({4:F1},{5:F1},{6:F1}), hideSpawnEffect: {7}",
            slaveId, itemId, slotType, slot, x, y, z, hideSpawnEffect);

        var character = Connection.ActiveChar;
        if (character?.ParentWorld == null)
            return;

        // The summon item is authoritative for what gets spawned — the client supplies the slave
        // id, but taking it on trust would let any item summon any slave.
        var item = ItemManager.Instance.GetItemByItemId(itemId);
        if (item == null || item.OwnerId != character.Id || !AuctionHouseRules.IsPlayerHeldItem(item))
        {
            Logger.Warn("SpawnSlave: {0} does not hold item {1}", character.Name, itemId);
            return;
        }

        if (item.Template is not SummonSlaveTemplate summonTemplate)
        {
            Logger.Warn(
                "SpawnSlave: item {0} (template {1}) used by {2} does not summon a slave",
                itemId, item.TemplateId, character.Name);
            return;
        }

        if (summonTemplate.SlaveId != slaveId)
        {
            Logger.Warn(
                "SpawnSlave: {0} asked for slave {1} with item {2}, which summons {3} — using the item",
                character.Name, slaveId, itemId, summonTemplate.SlaveId);
        }

        // Client picks the spot (SummonPos target); orientation comes from zRot.
        using var transform = character.Transform.CloneDetached();
        transform.World.SetPosition(x, y, z);
        transform.World.Rotate(transform.World.Rotation with { Z = zRot });

        character.ParentWorld.SlaveManager.Create(
            character, null, summonTemplate.SlaveId, item, hideSpawnEffect, transform);
    }
}
