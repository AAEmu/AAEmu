using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Opens/refreshes the prestige-shop buff view - read as "the client wants the current buff-grade state
/// resent" regardless of TypeValue/ResponseOnly's exact unconfirmed semantics, since SCExpeditionBuffsPacket
/// is a full resync anyway. 2026-08-27: was fully parsed but never wired to anything, and never even
/// registered in GameNetwork's dispatch table - same bug class as CSExpeditionLevelUpPacket.
/// 2026-08-28: gating the resend on Enter was wrong - live logs show the real client never sets it true
/// on any of the many shop-open requests observed, so SendExpeditionBuffs never fired and the shop
/// permanently showed stale (all-unpurchased) grades, causing repeat "buy grade 1" attempts on buffs
/// already owned. Resync unconditionally instead - cheap, and this packet's own risk model is
/// "renders incorrectly or dropped, not a crash" either way.
/// </summary>
public class CSExpeditionBuffPacket() : GamePacket(CSOffsets.CSExpeditionBuffPacket, 1)
{
    public int TypeValue { get; private set; }
    public bool Enter { get; private set; }
    public bool ResponseOnly { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        Enter = stream.ReadBoolean();
        ResponseOnly = stream.ReadBoolean();

        if (Connection.ActiveChar?.Expedition != null)
            ExpeditionManager.Instance.SendExpeditionBuffs(Connection.ActiveChar);
    }
}
