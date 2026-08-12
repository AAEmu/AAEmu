using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Reports the outcome of one proficiency rank change.
/// </summary>
/// <remarks>
/// Schema: <c>bool isUpgrade</c> followed by one actability entry, written by
/// <see cref="Actability.Write"/>.
/// <para>
/// The entry is the same element the full actability list sends, so the two have to stay identical;
/// that is why neither packet writes those fields itself. Sending anything shorter - an id alone, or an
/// id without its point - leaves the body under length and the change is not applied, which shows up as
/// a rank that appears only after the next login, once the whole list is sent again.
/// </para>
/// <para>
/// The entry is taken from the Actability rather than from loose values, so the point published here is
/// always the one the server holds. That matters on a downgrade, where the point is clamped to the
/// target rank's limit as part of the same change.
/// </para>
/// </remarks>
public class SCExpertLimitModifiedPacket(bool isUpgrade, Actability actability)
    : GamePacket(SCOffsets.SCExpertLimitModifiedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isUpgrade);
        actability.Write(stream);
        return stream;
    }
}
