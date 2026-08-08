using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Continues listing the selected mailbox category.</summary>
public class CSListMailContinuePacket() : GamePacket(CSOffsets.CSListMailContinuePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var kind = stream.ReadByte();
        Logger.Debug("ListMailContinue kind={0}", kind);
        // Nothing more to send for our page size (≤100 headers in one OpenMailbox).
    }
}
