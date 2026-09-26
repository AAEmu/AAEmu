using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells an applicant the recruiter approved them: u64 type (the post's owner id), u32 role
/// The client opens the 60 s accept popup and answers with
/// CSRaidApplicantAcceptReply, which is what seats them.
/// </summary>
public class SCRaidApplicantAcceptPacket(ulong @type, uint role) : GamePacket(SCOffsets.SCRaidApplicantAcceptPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(role);
        return stream;
    }
}
