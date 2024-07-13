using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionApplicantAddPacket : GamePacket
{
    private readonly uint _expeditionId;

    public SCExpeditionApplicantAddPacket(uint expeditionId) : base(SCOffsets.SCExpeditionApplicantAddPacket, 5)
    {
        _expeditionId = expeditionId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_expeditionId);
        return stream;
    }
}
