using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x095 — reply to CSChangeSlaveEquipment. Wire matches the CS body
/// characterId(u64), tl(u16), dbSlaveId(u32), bts(u8), num(u8),
/// num × { Item item1, Item item2, u8 slot1Type, u8 slot1, u8 slot2Type, u8 slot2, expireTime(s64) },
/// success(u8). characterId is policy +152 = u64 (same as SCMySlave maxHp).
///
/// The entries must keep the request's order (item1↔slot1, item2↔slot2, slot2 = the ship slot).
/// and then copies <b>item2</b> over <b>inventory slot1</b>, so both items have to be the pre-move
/// contents. Swapping the two entries around makes the client drop the item it just unequipped.
///
/// The client locks both slot keys when it sends the request and only unlocks them here
/// locked ("Can't equip; slot is locked."). success == 0 unlocks without moving anything.
/// </summary>
public class SCSlaveEquipmentChangedPacket : GamePacket
{
    private readonly ushort _slaveTlId;
    private readonly uint _characterId;
    private readonly uint _dbSlaveId;
    private readonly bool _bts;
    private readonly byte _num;
    private readonly bool _success;
    private readonly ItemAndLocation _first;
    private readonly ItemAndLocation _second;
    private readonly DateTime _expireTime;

    public SCSlaveEquipmentChangedPacket(ItemAndLocation first,
        ItemAndLocation second,
        ushort slaveTlId,
        uint characterId,
        uint dbSlaveId,
        bool bts,
        bool success,
        DateTime expireTime)
        : base(SCOffsets.SCSlaveEquipmentChangedPacket, 1)
    {
        _first = first;
        _second = second;
        _slaveTlId = slaveTlId;
        _characterId = characterId;
        _dbSlaveId = dbSlaveId;
        _bts = bts;
        _num = 1;
        _success = success;
        _expireTime = expireTime;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)_characterId);
        stream.Write(_slaveTlId);
        stream.Write(_dbSlaveId);
        stream.Write(_bts);
        stream.Write(_num);

        if (_first.Item == null)
            stream.Write(0u);
        else
            stream.Write(_first.Item);

        if (_second.Item == null)
            stream.Write(0u);
        else
            stream.Write(_second.Item);

        stream.Write((byte)_first.SlotType);
        stream.Write(_first.SlotNumber);
        stream.Write((byte)_second.SlotType);
        stream.Write(_second.SlotNumber);
        stream.Write(_expireTime);
        stream.Write(_success);

        return stream;
    }
}
