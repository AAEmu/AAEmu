using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client skillsaver delete. Success fires <c>SCAbilitySetUpdated</c> (<c>responseType=3</c>).
/// </summary>
public class CSDeleteAbilitySetPacket() : GamePacket(CSOffsets.CSDeleteAbilitySetPacket, 1)
{
    public sbyte SlotIndex { get; private set; }

    public override void Read(PacketStream stream)
    {
        SlotIndex = stream.ReadSByte();
        Connection.ActiveChar?.AbilitySets?.TryDelete(SlotIndex);
    }
}
