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
    /// <remarks>
    /// A refusal that is *not* distinct is the deliberate one: a delete of a roster the caller does
    /// not own answers with the delete failure rather than with <c>CONTENT_ROSTER_NOT_USABLE_OWNER</c>,
    /// whose text is about being unable to *save* a roster and which would also tell a crafted
    /// request that the id exists. So the rule this pins is "no refusal is silent, and a refusal the
    /// caller can provoke does not hand back a save-only error" - not "every refusal is distinct".
    /// </remarks>
    [Test]
    public async Task EveryRosterDeleteRefusal_NamesANonZeroErrorAndNoSaveOnlyText()
    {
        var refusals = Enum.GetValues<ContentRosterDeleteResult>()
            .Where(result => result != ContentRosterDeleteResult.Success)
            .ToList();
        await Assert.That(refusals.Count).IsGreaterThan(0);

        var errors = refusals
            .Select(result => (ushort)new ContentRosterDeleteOutcome(result, 0).Error)
            .ToList();
        await Assert.That(errors).DoesNotContain((ushort)ErrorMessageType.NoErrorMessage);

        // A delete must never answer with a text that describes the save path.
        const ushort notUsableOwner = (ushort)ErrorMessageType.ContentRosterNotUsableOwner;
        const ushort saveCoolTime = (ushort)ErrorMessageType.ContentRosterSaveCoolTime;
        await Assert.That(errors).DoesNotContain(notUsableOwner);
        await Assert.That(errors).DoesNotContain(saveCoolTime);

        // And a not-owned delete must not be distinguishable from a delete refused for any other
        // reason, so a crafted request cannot use it to probe for an id that exists.
        var notOwner = (ushort)new ContentRosterDeleteOutcome(ContentRosterDeleteResult.NotOwner, 0).Error;
        var invalid = (ushort)new ContentRosterDeleteOutcome(ContentRosterDeleteResult.InvalidRequest, 0).Error;
        await Assert.That(notOwner).IsEqualTo(invalid);
    }

    /// <summary>
    /// The roster error ids have to be the ones the client resolves by number.
    /// </summary>
    /// <remarks>
    /// The client renders an error from <c>ui_texts</c> using the number on the wire, and the
    /// shipped <c>CONTENT_ROSTER_*</c> texts sit at 11161-11168. The 1200-1207 range the server
    /// enum originally used resolves to party and loot-rule keywords in the same table, so every
    /// roster refusal rendered an unrelated message. These values were confirmed against the
    /// shipped content; the assertions below pin them so an accidental revert is caught here, since
    /// the unit tests cannot read the content database themselves.
    /// </remarks>
    [Test]
    public async Task RosterErrorIds_AreTheOnesTheClientResolves()
    {
        var expected = new Dictionary<ErrorMessageType, ushort>
        {
            [ErrorMessageType.ContentRosterDeleteFailed] = 11161,
            [ErrorMessageType.ContentRosterListFull] = 11162,
            [ErrorMessageType.ContentRosterNotFound] = 11163,
            [ErrorMessageType.ContentRosterNotFoundTeam] = 11164,
            [ErrorMessageType.ContentRosterNotUsableOwner] = 11165,
            [ErrorMessageType.ContentRosterSaveMemberSize] = 11166,
            [ErrorMessageType.ContentRosterSaveCoolTime] = 11167,
            [ErrorMessageType.ContentRosterSaveFailed] = 11168,
        };

        foreach (var (error, id) in expected)
            await Assert.That((ushort)error).IsEqualTo(id);
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
