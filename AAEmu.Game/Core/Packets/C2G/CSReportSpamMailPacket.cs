using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Reports a received player letter as spam. The body carries the letter id and the
/// sender name the report refers to; both are handed to the mailbox for validation.
/// </summary>
public class CSReportSpamMailPacket() : GamePacket(CSOffsets.CSReportSpamMailPacket, 1)
{
    public ulong Type { get; private set; }
    public string Sender { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadUInt64();
        Sender = stream.ReadString();

        Logger.Debug("ReportSpamMail, mailId: {0}, sender: {1}", Type, Sender);
        Connection.ActiveChar?.Mails.ReportSpam((long)Type, Sender);
    }
}
