using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTakeAttachmentSequentially() : GamePacket(CSOffsets.CSTakeAttachmentSequentially, 1)
{
    public override void Read(PacketStream stream)
    {
        var mailId = stream.ReadInt64();
        Logger.Debug("TakeAttachmentSequentially, mailId: {0}", mailId);
        // GetAttached rejects a missing or foreign mail and tells the client the
        // row is gone. Looking the mail up here used to NRE and leave the
        // mailbox spinner running.
        Connection.ActiveChar.Mails.GetAttached(mailId, true, true, true);
    }
}
