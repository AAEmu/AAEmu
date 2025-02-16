using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListMailContinuePacket : GamePacket
{
    public CSListMailContinuePacket() : base(CSOffsets.CSListMailContinuePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mailBoxListKind = stream.ReadByte();

        Logger.Debug($"ListMailContinue: mailBoxListKind {mailBoxListKind}");

        Connection.ActiveChar.Mails.SendMailListContinue(mailBoxListKind);
    }
}
