using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Wire contract for the two definitive answers the content-roster delete and the survey-form
/// reply send back. Refusals have to reach the client as a definite packet, so the field order
/// and the refusal-to-error mapping are pinned here.
/// </summary>
public class ContentRosterSurveyAnswerPacketTests
{
    [Test]
    public async Task RosterDeleteAnswer_WritesResultThenIsExpiredThenErrorMessage()
    {
        var stream = new SCContentRosterDeletePacket(
            result: false, isExpired: true, ErrorMessageType.ContentRosterNotFound)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)ErrorMessageType.ContentRosterNotFound);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task RosterDeleteAnswer_CarriesTheSuccessFlagInTheFirstByte()
    {
        var stream = new SCContentRosterDeletePacket(
            result: true, isExpired: false, ErrorMessageType.NoErrorMessage)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SurveyAnswer_WritesErrorMessageThenTypeThenResult()
    {
        var stream = new SCSurveyFormSavePacket(
            ErrorMessageType.SurveyFormAlreadyDone, 7u, result: false)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)ErrorMessageType.SurveyFormAlreadyDone);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SurveyAnswer_WritesTheRepliedFormIdAheadOfTheResult()
    {
        var stream = new SCSurveyFormSavePacket(
            ErrorMessageType.NoErrorMessage, 41488u, result: true)
            .Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)0);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(41488u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    /// <summary>
    /// Every refusal must name a distinct non-zero error, otherwise the client is told "no" with
    /// no reason. Nothing may be answered with silence.
    /// </summary>
    [Test]
    public async Task EveryRosterDeleteRefusal_NamesADistinctNonZeroError()
    {
        var refusals = Enum.GetValues<ContentRosterDeleteResult>()
            .Where(result => result != ContentRosterDeleteResult.Success)
            .ToList();
        await Assert.That(refusals.Count).IsGreaterThan(0);

        var errors = refusals.Select(result => (ushort)new ContentRosterDeleteOutcome(result, 0).Error).ToList();
        await Assert.That(errors).DoesNotContain((ushort)ErrorMessageType.NoErrorMessage);
        await Assert.That(errors.Distinct().Count()).IsEqualTo(refusals.Count);
    }

    [Test]
    public async Task EverySurveyRefusal_NamesADistinctNonZeroError()
    {
        var refusals = Enum.GetValues<SurveyFormReplyResult>()
            .Where(result => result != SurveyFormReplyResult.Success)
            .ToList();
        await Assert.That(refusals.Count).IsGreaterThan(0);

        var errors = refusals
            .Select(result => (ushort)new SurveyFormReplyOutcome(1u, result).Error)
            .ToList();
        await Assert.That(errors).DoesNotContain((ushort)ErrorMessageType.NoErrorMessage);
        await Assert.That(errors.Distinct().Count()).IsEqualTo(refusals.Count);
    }

    [Test]
    public async Task SuccessResults_MapToNoErrorMessage()
    {
        await Assert.That((ushort)new ContentRosterDeleteOutcome(ContentRosterDeleteResult.Success, 1).Error)
            .IsEqualTo((ushort)ErrorMessageType.NoErrorMessage);
        await Assert.That((ushort)new SurveyFormReplyOutcome(1u, SurveyFormReplyResult.Success).Error)
            .IsEqualTo((ushort)ErrorMessageType.NoErrorMessage);
    }
}
