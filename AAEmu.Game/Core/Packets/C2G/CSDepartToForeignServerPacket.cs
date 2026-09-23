using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.CrossServer;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client asks to leave this server for another one.
/// </summary>
/// <remarks>
/// Wire: 10.0.2.13 <c>CSDepartToForeignServerPacket</c>, opcode 0x1C7, zero fields
/// (protocol-10.0.2.13/catalog_CS.md row 394; the shipped client's packet structs list the class
/// with <c>fields: []</c>), so the frame
/// must carry no body — anything else fails loudly. The destination is therefore not on the
/// wire either: it is resolved from content (see <see cref="ICrossServerDirectory"/>).
/// </remarks>
public class CSDepartToForeignServerPacket() : GamePacket(CSOffsets.CSDepartToForeignServerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes != 0)
            throw new InvalidDataException(
                $"Expected a 0-byte departure body (0x1C7 has no fields), got {stream.LeftBytes} bytes.");

        var connection = Connection;
        var character = connection?.ActiveChar;
        if (connection == null || character == null)
        {
            Logger.Error("CSDepartToForeignServer arrived outside a world session; departure refused.");
            return;
        }

        if (!connection.ForeignPassportIssued)
        {
            Logger.Error(
                "Cross-server departure refused for {0}: no passport was issued on this connection.",
                character.Name);
            return;
        }

        // characters.transfer_request_time is the park marker and is written by every
        // Character.Save(), so the live character must carry the same timestamp the journal is
        // about to persist; on refusal the previous value is put back untouched.
        var parkedAtUtc = DateTime.UtcNow;
        var previousTransferRequest = character.TransferRequestTime;
        character.TransferRequestTime = parkedAtUtc;

        var result = CrossServerTransferManager.Instance.RequestDeparture(
            character.Id, connection.AccountId, targetServerKey: null, parkedAtUtc,
            character.Money, character.Money2, character.AaPoint);

        if (result.Outcome != CrossServerTransferOutcome.Granted)
        {
            character.TransferRequestTime = previousTransferRequest;
            // Refusals are logged, not answered: the 10.0.2.13 corpus pins no refusal frame for
            // 0x1C7, and inventing one would put bytes on the wire the client never parses.
            Logger.Error(
                "Cross-server departure refused for {0} (ObjId {1}): {2}.",
                character.Name, character.ObjId, result.Outcome);
            return;
        }

        connection.SendPacket(new SCDepartureServerGrantedPacket());
    }
}
