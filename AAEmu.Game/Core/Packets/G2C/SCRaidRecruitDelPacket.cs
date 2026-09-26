using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A post is gone: u64 type, its owner id. Sent to the recruiting team and
/// to every applicant when the poster deletes it, logs off, changes faction, leaves the team, the team
/// disbands, or the departure time plus 20 minutes passes.
/// </summary>
public class SCRaidRecruitDelPacket(ulong @type) : GamePacket(SCOffsets.SCRaidRecruitDelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        return stream;
    }
}
