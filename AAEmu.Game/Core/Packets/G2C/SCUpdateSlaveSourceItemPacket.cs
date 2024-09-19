using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUpdateSlaveSourceItemPacket : GamePacket
{
    private readonly uint _bc;
    private readonly ulong _itemId;
    private readonly int _health;
    private readonly byte _EquipSlot;

    public SCUpdateSlaveSourceItemPacket(uint bc, ulong itemId, int health, byte EquipSlot)
        : base(SCOffsets.SCUpdateSlaveSourceItemPacket, 5)
    {
        _bc = bc;
        _itemId = itemId;
        _health = health;
        _EquipSlot = EquipSlot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_bc);
        stream.Write(_itemId);
        stream.Write(_health);
        stream.Write(_EquipSlot);
        return stream;
    }
}
