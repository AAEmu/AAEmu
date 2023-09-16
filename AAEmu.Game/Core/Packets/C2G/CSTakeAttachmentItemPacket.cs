using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTakeAttachmentItemPacket : GamePacket
{
    public CSTakeAttachmentItemPacket() : base(CSOffsets.CSTakeAttachmentItemPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mailId = stream.ReadInt64();
        var itemId = stream.ReadUInt32();
        var id = stream.ReadUInt64();
        var grade = stream.ReadByte();
        var flags = stream.ReadByte();
        var count = stream.ReadUInt32();
        var detailType = stream.ReadByte();

        var creationTime = stream.ReadDateTime();
        var lifespanMins = stream.ReadUInt32();
        var type2 = stream.ReadUInt32(); // type(id)
        var worldId = stream.ReadByte();
        var unsecureDateTime = stream.ReadDateTime();
        var unpackDateTime = stream.ReadDateTime();

        var slotType = stream.ReadByte();
        var slot = stream.ReadByte();

        Connection.ActiveChar.Mails.GetAttached(mailId, false, true, false, id);
    }
}
