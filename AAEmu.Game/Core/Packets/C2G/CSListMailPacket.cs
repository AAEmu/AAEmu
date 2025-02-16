using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListMailPacket : GamePacket
{
    public CSListMailPacket() : base(CSOffsets.CSListMailPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var mailBoxListKind = stream.ReadByte();
        var startIdx = stream.ReadInt32();
        var sentCnt = stream.ReadInt32();
        var isRecover = stream.ReadBoolean();
        var isTest = stream.ReadBoolean();

        Logger.Debug($"CSListMailPacket: mailBoxListKind={mailBoxListKind}, startIdx={startIdx}, sentCnt={sentCnt}, isRecover={isRecover}, isTest={isTest}");

        Connection.ActiveChar.Mails.SendMailList(mailBoxListKind, startIdx, sentCnt, isRecover, isTest);
    }
}
