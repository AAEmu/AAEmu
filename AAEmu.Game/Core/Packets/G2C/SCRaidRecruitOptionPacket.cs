using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The post's Auto-Invite / Manual Invite setting after CSRaidRecruitOption: bool autoJoin
/// (x2game-dev.dll FUN_39c54840), sent to the recruiting team.
/// </summary>
public class SCRaidRecruitOptionPacket(bool autoJoin) : GamePacket(SCOffsets.SCRaidRecruitOptionPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(autoJoin);
        return stream;
    }
}
