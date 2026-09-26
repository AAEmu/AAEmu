using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Drops one application from the applicant's client: u64 type, the post's owner id. Sent for a withdrawal, a declined accept and whenever the post itself goes away.
/// </summary>
public class SCRaidApplicantDelPacket(ulong @type) : GamePacket(SCOffsets.SCRaidApplicantDelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        return stream;
    }
}
