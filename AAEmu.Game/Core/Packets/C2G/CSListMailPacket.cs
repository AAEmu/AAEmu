using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Lists a page from the inbox, sent, or commercial mailbox.</summary>
public class CSListMailPacket() : GamePacket(CSOffsets.CSListMailPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var kind = stream.ReadByte();
        var startIdx = stream.ReadUInt32();
        var sentCnt = stream.ReadUInt32();
        var isRecover = stream.ReadBoolean();
        var isTest = stream.ReadBoolean();

        Logger.Debug(
            "CSListMail kind={0} start={1} sentCnt={2} recover={3} test={4}",
            kind, startIdx, sentCnt, isRecover, isTest);

        Connection.ActiveChar.Mails.OpenMailbox(kind, startIdx, sentCnt);
    }
}
