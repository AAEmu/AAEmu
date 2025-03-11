using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Mails.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTakeAllAttachmentItemPacket : GamePacket
{
    public CSTakeAllAttachmentItemPacket() : base(CSOffsets.CSTakeAllAttachmentItemPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mailId = stream.ReadInt64();
        Logger.Debug($"CSTakeAllAttachmentItemPacket {mailId} -> {Connection.ActiveChar.Name}");
        if (Connection.ActiveChar.Mails.GetAttached(mailId, true, true, true))
        {
            Connection.ActiveChar.SendPacket(new SCMailStatusUpdatedPacket(false, mailId, MailStatus.Read));
            Connection.ActiveChar.Mails.DeleteMail(mailId, false);
            Connection.ActiveChar.Mails.SendUnreadMailCount();
        }
        else
            Logger.Debug($"CSTakeAllAttachmentItemPacket - Failed for: {mailId} -> {Connection.ActiveChar.Name}");
    }
}
