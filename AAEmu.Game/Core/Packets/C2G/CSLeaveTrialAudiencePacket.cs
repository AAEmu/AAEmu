using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveTrialAudiencePacket() : GamePacket(CSOffsets.CSLeaveTrialAudiencePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        Logger.Warn("LeaveTrialAudience");
    }
}
