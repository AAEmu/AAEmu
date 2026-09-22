using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells an applicant the recruiter declined them: u64 type, the post's owner id (x2game-dev.dll
/// FUN_39c688d0; ui_texts 8856).
/// </summary>
public class SCRaidApplicantRejectPacket(ulong @type) : GamePacket(SCOffsets.SCRaidApplicantRejectPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        return stream;
    }
}
