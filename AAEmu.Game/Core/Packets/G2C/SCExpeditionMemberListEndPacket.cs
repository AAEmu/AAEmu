using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionMemberListEndPacket : GamePacket
{
    private readonly uint _total;
    private readonly uint _id;
    private readonly List<ExpeditionMember> _members;

    public SCExpeditionMemberListEndPacket(Expedition expedition) : base(SCOffsets.SCExpeditionMemberListEndPacket, 5)
    {
        _total = (uint)expedition.Members.Count;
        _id = expedition.Id;
        _members = expedition.Members; // TODO max 20
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_id); // expedition id
        return stream;
    }
}
