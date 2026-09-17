using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client cancelled the verification it was shown for a protected account. The sequence number is the one
/// the guard handed out when it held an action back; a cancel for any other number is ignored.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each field name
/// alongside the value: one i32.
/// </remarks>
public class CSCancelSensitiveOperationVerify() : GamePacket(CSOffsets.CSCancelSensitiveOperationVerify, 1)
{
    public int SeqNum { get; private set; }

    public override void Read(PacketStream stream)
    {
        SeqNum = stream.ReadInt32();

        var character = Connection?.ActiveChar;
        if (character != null)
            SensitiveOperationGuard.CancelVerification(character, SeqNum);
    }
}
