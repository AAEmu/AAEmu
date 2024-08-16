using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionMemberListEndPacket : GamePacket
{
    private readonly uint _total;
    private readonly uint _expeditionId;

    public SCExpeditionMemberListEndPacket( uint total, FactionsEnum expeditionId) : base(SCOffsets.SCExpeditionMemberListEndPacket, 5)
    {
        _total = total;
        _expeditionId = (uint)expeditionId;
    }

    public SCExpeditionMemberListEndPacket(Expedition expedition) : base(SCOffsets.SCExpeditionMemberListEndPacket, 5)
    {
        _total = (uint)expedition.Members.Count;
        _expeditionId = (uint)expedition.Id;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_expeditionId); // expeditionId
        return stream;
    }
}
