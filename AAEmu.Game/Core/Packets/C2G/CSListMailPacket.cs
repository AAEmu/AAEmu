using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListMailPacket() : GamePacket(CSOffsets.CSListMailPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // 10.0.2.13 wire: [u8 mailBoxListKind][i32 startIdx][i32 sentCnt][bool isRecover][bool isTest].
        // The kind selects which mailbox tab (1 = inbox, 3 = commercial/marketplace, ...) and must be
        // echoed back in SCMailListEnd so the client finalizes the matching list.
        var mailBoxListKind = stream.ReadByte();
        _ = stream.ReadInt32();   // startIdx  (server sends the whole list at once)
        _ = stream.ReadInt32();   // sentCnt
        _ = stream.ReadBoolean(); // isRecover
        _ = stream.ReadBoolean(); // isTest

        Connection.ActiveChar.Mails.OpenMailbox(mailBoxListKind);
    }
}
