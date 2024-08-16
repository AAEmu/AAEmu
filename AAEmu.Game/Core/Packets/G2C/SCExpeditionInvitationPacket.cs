using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionInvitationPacket : GamePacket
{
    private readonly uint _invitorId;
    private readonly string _invitorName;
    private readonly uint _factionId;
    private readonly string _factionName;

    public SCExpeditionInvitationPacket(uint invitorId, string invitorName, FactionsEnum factionId, string factionName)
        : base(SCOffsets.SCExpeditionInvitationPacket, 5)
    {
        _invitorId = invitorId;
        _invitorName = invitorName;
        _factionId = (uint)factionId;
        _factionName = factionName;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_invitorId);
        stream.Write(_invitorName);
        stream.Write(_factionId);
        stream.Write(_factionName);
        return stream;
    }
}
