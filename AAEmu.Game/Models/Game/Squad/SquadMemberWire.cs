using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Squad;

/// <summary>
/// SquadBase mask-bit-8 member blob. Every field goes through one bounds-checked copy of an
/// exact width with no padding or alignment, and the optional-field test around the key is a
/// constant true that reads nothing, so the row is exactly <see cref="EmbeddedMask8PayloadSize"/>
/// bytes in this order: key u64, level u8, ability x3 u8, elo i32, role u8, ready u8, offline u8.
///
/// An extra byte per row is not harmless: it shifts every later field, and the leader key that
/// follows the member array then reads past the blob limit, which the client answers with an
/// uncaught throw. Members carry no name here; SCJoinSquadMember 0x311 supplies that.
/// </summary>
public static class SquadMemberWire
{
    /// <summary>Per-member payload inside SquadBase mask 8.</summary>
    public const int EmbeddedMask8PayloadSize = 19;

    public static void WriteEmbeddedMask8(PacketStream stream, SquadMember member, byte worldId)
    {
        stream.Write(SquadWorldCharKey.Make(member.CharacterId, worldId));
        stream.Write(member.Level);
        stream.Write(member.Ability1);
        stream.Write(member.Ability2);
        stream.Write(member.Ability3);
        stream.Write(member.EloRating);
        stream.Write((byte)member.Role);
        stream.Write(member.Ready ? (byte)1 : (byte)0);
        stream.Write(member.Offline ? (byte)1 : (byte)0);
    }
}
