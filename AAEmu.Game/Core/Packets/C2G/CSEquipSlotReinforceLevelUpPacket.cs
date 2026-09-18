using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks to take the next reinforcement level on one equip slot. The client only offers the button when
/// the slot's bar is full, but the bar and the price are ours to check: a request that arrives early,
/// for a slot with no ladder, or without the item the step costs is refused.
/// </summary>
/// <remarks>
/// The body is the slot alone — the item the step costs comes from the ladder, not from the client.
/// </remarks>
public class CSEquipSlotReinforceLevelUpPacket() : GamePacket(CSOffsets.CSEquipSlotReinforceLevelUpPacket, 1)
{
    public byte EquipSlot { get; private set; }

    public override void Read(PacketStream stream)
    {
        EquipSlot = stream.ReadByte();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (EquipSlotReinforceGameData.Instance.Ladder(EquipSlot).Count == 0)
        {
            Logger.Warn("Equip slot reinforce: {0} asked to level slot {1}, which has no ladder",
                character.Name, EquipSlot);
            return;
        }

        var change = character.EquipSlotReinforces.LevelUp(EquipSlot);
        Logger.Info("Equip slot reinforce: {0} slot {1} -> {2}", character.Name, EquipSlot, change);
    }
}
