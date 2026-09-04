using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// 2026-09-02: was a fully-parsed no-op stub - see ExpeditionManager.CancelProtection's doc comment.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSCancelExpeditionProtectionPacket() : GamePacket(CSOffsets.CSCancelExpeditionProtectionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        ExpeditionManager.Instance.CancelProtection(Connection.ActiveChar);
    }
}
