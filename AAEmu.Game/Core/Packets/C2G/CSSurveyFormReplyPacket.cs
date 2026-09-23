using System.IO;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Publisher survey-form reply (10.0.2.13 <c>CSSurveyFormReplyPacket</c>).
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// u32 type (the survey_forms id), u8 forceFuture
/// The recovered serializer has no answer payload; see the GF-S18 notes for that gap.
/// </remarks>
public class CSSurveyFormReplyPacket() : GamePacket(CSOffsets.CSSurveyFormReplyPacket, 1)
{
    private const int BodySize = sizeof(uint) + sizeof(byte);

    public uint TypeValue { get; private set; }

    public byte ForceFuture { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.Pos + BodySize > stream.Count)
            throw new InvalidDataException(
                $"CSSurveyFormReplyPacket: body is {stream.Count} byte(s); expected u32 type + u8 forceFuture.");

        TypeValue = stream.ReadUInt32();
        ForceFuture = stream.ReadByte();

        if (stream.Overran)
            throw new InvalidDataException("CSSurveyFormReplyPacket: read past the end of the body.");

        if (Connection is { ActiveChar: not null } connection)
        {
            var outcome = SurveyFormService.Instance.Reply(
                connection.AccountId, TypeValue, connection.ActiveChar.Id, ServerCalendar.UtcNow, ForceFuture);

            connection.SendPacket(new SCSurveyFormSavePacket(
                outcome.Success ? ErrorMessageType.NoErrorMessage : ErrorMessageType.InternalError,
                TypeValue,
                (byte)outcome.Result));
        }
    }
}
