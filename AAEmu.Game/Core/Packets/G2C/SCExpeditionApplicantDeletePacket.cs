using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionApplicantDeletePacket : GamePacket
{
    private readonly uint _expeditionId;

    public SCExpeditionApplicantDeletePacket(FactionsEnum expeditionId) : base(SCOffsets.SCExpeditionApplicantDeletePacket, 5)
    {
        _expeditionId = (uint)expeditionId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_expeditionId);
        return stream;
    }
}
