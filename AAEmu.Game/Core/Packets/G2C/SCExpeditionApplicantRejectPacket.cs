using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionApplicantRejectPacket : GamePacket
{
    private readonly uint _characterId;

    public SCExpeditionApplicantRejectPacket(uint characterId) : base(SCOffsets.SCExpeditionApplicantRejectPacket, 5)
    {
        _characterId = characterId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_characterId);
        return stream;
    }
}
