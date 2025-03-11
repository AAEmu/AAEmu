using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCScheduleItemSentPacket : GamePacket
{
    private readonly uint _itemTemplateId;
    private readonly bool _byMail;

    public SCScheduleItemSentPacket(uint itemTemplateId, bool byMail) : base(SCOffsets.SCScheduleItemSentPacket, 5)
    {
        _itemTemplateId = itemTemplateId;
        _byMail = byMail;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemTemplateId);
        stream.Write(_byMail);
        return stream;
    }
}
