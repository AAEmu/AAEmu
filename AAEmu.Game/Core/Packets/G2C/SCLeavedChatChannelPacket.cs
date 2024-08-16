using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLeavedChatChannelPacket : GamePacket
{
    private readonly short _type;
    private readonly short _subType;
    private readonly uint _factionId;

    public SCLeavedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId) : base(SCOffsets.SCLeavedChatChannelPacket, 5)
    {
        _type = (short)type;
        _subType = subType;
        _factionId = (uint)factionId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_type);
        stream.Write(_subType);
        stream.Write(_factionId);
        return stream;
    }
}
