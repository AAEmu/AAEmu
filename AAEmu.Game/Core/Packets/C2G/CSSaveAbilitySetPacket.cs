using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client skillsaver save — snapshots the current triad + learned skills into <paramref name="SlotIndex"/>.
/// Success fires <c>SCAbilitySetUpdated</c> (<c>responseType=1</c>) → client <c>ABILITY_SET_CHANGED</c> toast.
/// </summary>
public class CSSaveAbilitySetPacket() : GamePacket(CSOffsets.CSSaveAbilitySetPacket, 1)
{
    public sbyte SlotIndex { get; private set; }

    public override void Read(PacketStream stream)
    {
        SlotIndex = stream.ReadSByte();
        Connection.ActiveChar?.AbilitySets?.TrySave(SlotIndex);
    }
}
