using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRecruitmentDeletePacket : GamePacket
{
    public SCExpeditionRecruitmentDeletePacket() : base(SCOffsets.SCExpeditionRecruitmentDeletePacket, 5)
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        // empty body
        return stream;
    }
}
