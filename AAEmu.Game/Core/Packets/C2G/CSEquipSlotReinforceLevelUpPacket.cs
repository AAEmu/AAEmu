using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte equipSlot
/// </remarks>
public class CSEquipSlotReinforceLevelUpPacket() : GamePacket(CSOffsets.CSEquipSlotReinforceLevelUpPacket, 1)
{
    public sbyte EquipSlot { get; private set; }

    public override void Read(PacketStream stream)
    {
        EquipSlot = stream.ReadSByte();
    }
}
