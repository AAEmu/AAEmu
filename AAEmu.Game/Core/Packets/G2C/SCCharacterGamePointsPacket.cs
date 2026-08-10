using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Fills X2Player:GetGamePoints() on the client - the table the character sheet reads for honor,
/// vocation and leadership.
/// </summary>
/// <remarks>
/// The slots are the fourteen dwords at ClientPlayer+0xec0, and GetGamePoints (.text 0x84b8d0) names
/// four of them by reading a field and pairing it with a key string:
///
///   +0xec0  slot 0   honorPointStr
///   +0xec4  slot 1   livingPointStr
///   +0xeec  slot 11  leadershipPointStr            the lifetime total
///   +0xef0  slot 12  periodLeadershipPointStr      the sheet's "Last Season Leadership" row
///
/// which is what fixes the array's base, and so every other slot's offset, from the two ends inwards.
/// Slot 12 was already known from a diagnostic that wrote index*1000 into all 14 and watched that row
/// render 12000; the disassembly agrees and additionally identifies slot 11.
///
/// It was first guessed at slot 2, from the four field-name literals sitting contiguously in
/// x2game-dev.dll at 0x1138ea8..0x1138ef0 read back in MSVC's reverse emission order. That guess was
/// wrong - literal layout does not track slot order here, so do not re-derive an index that way.
///
/// SCHeroSeasonOff writes +0xef0 as well, so the two packets have to carry the same number; see
/// HeroManager.SendLeadership, which is the only thing that should be sending either.
/// </remarks>
public class SCCharacterGamePointsPacket(Character character)
    : GamePacket(SCOffsets.SCCharacterGamePointsPacket, 1)
{
    /// <summary>Number of u32 "moneyAmount" slots the client reads.</summary>
    public const int SlotCount = 14;

    /// <summary>Slot carrying the lifetime leadership total.</summary>
    public const int LifetimeLeadershipSlot = 11;

    /// <summary>Slot carrying periodLeadershipPoint. Confirmed in-game, not inferred.</summary>
    public const int PeriodLeadershipSlot = 12;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.HonorPoint);              // 0  honorPoint
        stream.Write(character.VocationPoint);           // 1  livingPoint (vocation badges)

        // 2..10 are point currencies nothing implements yet.
        for (var i = 2; i < LifetimeLeadershipSlot; i++)
            stream.Write(0);

        stream.Write(character.AccumulatedLeadershipPoint); // 11 leadershipPoint (lifetime)
        stream.Write(character.LeadershipPeriodPoint);   // 12 periodLeadershipPoint

        for (var i = PeriodLeadershipSlot + 1; i < SlotCount; i++)
            stream.Write(0);
        return stream;
    }
}
