using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// How many invitees have accepted so far, sent while an invitation is open so each client can
/// update the count on its join dialog.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: u32 accept, u32 maxEntry, bool rematching.
/// </remarks>
public class SCMatchingInvitationInfoPacket(uint accept, uint maxEntry, bool rematching) : GamePacket(SCOffsets.SCMatchingInvitationInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(accept);
        stream.Write(maxEntry);
        stream.Write(rematching);
        return stream;
    }
}
