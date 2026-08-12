using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Sends a batch of the character's actabilities.
/// </summary>
/// <remarks>
/// Schema: <c>bool last</c>, <c>u8 count</c>, then <c>count</c> actability entries written by
/// <see cref="Actability.Write"/>.
/// <para>
/// <c>count</c> is the only length a reader has, so it must match the number of entries written, and a
/// batch may hold at most <see cref="MaxEntries"/>. Entries are variable width, which is why they are
/// written through the shared entry writer rather than field by field here - the same entry appears on
/// its own in <see cref="SCExpertLimitModifiedPacket"/> and the two must not diverge.
/// </para>
/// <para><c>last</c> marks the final batch, so a caller sending several must set it on that one alone.</para>
/// </remarks>
public class SCActabilityPacket(bool last, Actability[] actabilities) : GamePacket(SCOffsets.SCActabilityPacket, 1)
{
    /// <summary>Most entries one batch can carry.</summary>
    public const int MaxEntries = 100;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)actabilities.Length);
        foreach (var actability in actabilities)
            actability.Write(stream);

        return stream;
    }
}
