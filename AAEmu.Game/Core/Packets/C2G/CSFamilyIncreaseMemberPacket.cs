using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Routes a member-capacity request to the content-row purchase and persistence path.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSFamilyIncreaseMemberPacket() : GamePacket(CSOffsets.CSFamilyIncreaseMemberPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (Connection.ActiveChar != null)
            FamilyManager.Instance.IncreaseMemberLimit(Connection.ActiveChar);
    }
}
