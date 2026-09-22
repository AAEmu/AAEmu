using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client's answer to a zone-permission ask: 1 = OK, 0 = Cancel. The ask lives server-side in the
/// Indun manager, so an answer without one changes nothing and is refused with a definitive error —
/// permission only ever moves for a character the server actually asked, and every answer gets a
/// result packet.
/// </summary>
/// <remarks>Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: u8 answer.
/// </remarks>
public class CSAnswerZonePermissionPacket() : GamePacket(CSOffsets.CSAnswerZonePermissionPacket, 1)
{
    public byte Answer { get; private set; }

    public override void Read(PacketStream stream)
    {
        Answer = stream.ReadByte();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var verdict = IndunManager.Instance.AnswerZonePermission(character, Answer);
        switch (verdict)
        {
            case ZonePermissionVerdict.Accepted:
            case ZonePermissionVerdict.Declined:
                // Settled either way: the permission state behind the ask is now final, and the refresh
                // (empty body) is what tells the requesting client so.
                character.SendPacket(new SCZonePermissionChangedPacket());
                break;
            default:
                // No ask, or a body byte the dialog cannot produce: nothing moved, and the client is
                // told so instead of being left waiting.
                character.SendErrorMessage(ErrorMessageType.InvalidStateInstance);
                break;
        }
    }
}
