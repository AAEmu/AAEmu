using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A single hero, added or updated.
/// </summary>
/// <remarks>
/// The whole reader is three instructions (.text 0xc4e790):
///
///   add  rcx, 0x10
///   call 0xb4aa60      ; the shared hero row reader
///
/// so the body is exactly one row in the same 33-byte layout SCHeroList uses - see HeroListEntry.
///
/// Sent alongside the full roster because the two appear to feed different places. SCHeroList alone
/// makes X2Hero:IsHero() true (the hero-character set at manager +0xE0) but leaves
/// X2Hero:GetHeroFactions() empty (the faction map at +0x100), which keeps the Current Heroes faction
/// combobox greyed and makes the tab report "No Heroes have been elected yet". A per-hero upsert is the
/// most likely thing to register the faction, since it is the packet retail would send when one hero
/// changes rather than when a season is published wholesale.
/// </remarks>
public class SCHeroInfoUpdatedPacket(HeroListEntry hero) : GamePacket(SCOffsets.SCHeroInfoUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(hero.Unk0);
        stream.Write(hero.CharId);
        stream.Write(hero.FactionId);
        stream.Write(hero.ExpeditionId);
        stream.Write(hero.Ranking);
        stream.Write(hero.Score);
        stream.Write(hero.AccumPoint);
        stream.Write(hero.Grade);
        return stream;
    }
}
