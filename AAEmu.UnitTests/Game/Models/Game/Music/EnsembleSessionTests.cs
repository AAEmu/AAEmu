using AAEmu.Game.Models.Game.Music;

namespace AAEmu.UnitTests.Game.Models.Game.Music;

public class EnsembleSessionTests
{
    private const uint Maestro = 0x100;
    private const uint First = 0x201;
    private const uint Second = 0x202;

    private static EnsembleSession NewSession() => new(Maestro, "Tester");

    [Test]
    public async Task NewSession_HasTheMaestroSeatedAndNothingElse()
    {
        var session = NewSession();

        await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro });
        await Assert.That(session.Invited.Count).IsEqualTo(0);
        await Assert.That(session.IsOpen).IsTrue();
        await Assert.That(session.AllPartsReady).IsFalse();
    }

    [Test]
    public async Task Invite_SeatsAtMostWhatTheClientCanDraw()
    {
        var session = NewSession();

        // the maestro already holds one of the five seats
        await Assert.That(session.Invite(First)).IsTrue();
        await Assert.That(session.Invite(Second)).IsTrue();
        await Assert.That(session.Invite(0x203)).IsTrue();
        await Assert.That(session.Invite(0x204)).IsTrue();
        await Assert.That(session.Invite(0x205)).IsFalse(); // sixth player

        // and asking twice changes nothing
        await Assert.That(session.Invite(First)).IsFalse();
        await Assert.That(session.Invite(Maestro)).IsFalse();
    }

    [Test]
    public async Task Accept_MovesAnInviteeIntoTheEnsemble()
    {
        var session = NewSession();
        session.Invite(First);

        await Assert.That(session.Accept(First)).IsEqualTo(EnsembleJoinResult.Accepted);
        await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro, First });
        await Assert.That(session.Invited.Count).IsEqualTo(0);

        // accepting again is harmless
        await Assert.That(session.Accept(First)).IsEqualTo(EnsembleJoinResult.Accepted);
    }

    [Test]
    public async Task Accept_RefusesAPlayerWhoWasNeverAsked()
    {
        var session = NewSession();

        await Assert.That(session.Accept(First)).IsEqualTo(EnsembleJoinResult.NotMember);
    }

    [Test]
    public async Task Reject_TakesTheInvitationBack()
    {
        var session = NewSession();
        session.Invite(First);

        await Assert.That(session.Reject(First)).IsEqualTo(EnsembleJoinResult.Rejected);
        await Assert.That(session.Invited.Count).IsEqualTo(0);
        await Assert.That(session.Reject(First)).IsEqualTo(EnsembleJoinResult.NotMember);
    }

    [Test]
    public async Task Parts_MustComeFromMembersAndAllOfThemMustArrive()
    {
        var session = NewSession();
        session.Invite(First);
        session.Invite(Second);
        session.Accept(First);

        await Assert.That(session.PartReady(Second)).IsFalse(); // still only invited

        await Assert.That(session.PartReady(Maestro)).IsTrue();
        await Assert.That(session.AllPartsReady).IsFalse();

        await Assert.That(session.PartReady(First)).IsTrue();
        await Assert.That(session.AllPartsReady).IsTrue();
        await Assert.That(session.PartReady(First)).IsFalse(); // its part is already in
    }

    [Test]
    public async Task Start_WaitsForEveryPartAndThenClosesTheEnsemble()
    {
        var session = NewSession();
        session.Invite(First);
        session.Accept(First);

        await Assert.That(session.Start()).IsFalse();

        session.PartReady(Maestro);
        session.PartReady(First);

        await Assert.That(session.Start()).IsTrue();
        await Assert.That(session.IsStarted).IsTrue();
        await Assert.That(session.IsOpen).IsFalse();

        // nobody joins an ensemble that is already playing
        session.Invite(Second);
        await Assert.That(session.Accept(Second)).IsEqualTo(EnsembleJoinResult.Closed);
    }

    [Test]
    public async Task LeaveAfterStart_RemovesOnlyTheMemberAndKeepsThePerformance()
    {
        var session = NewSession();
        session.Invite(First);
        session.Accept(First);
        session.PartReady(Maestro);
        session.PartReady(First);
        await Assert.That(session.Start()).IsTrue();

        await Assert.That(session.Leave(First)).IsTrue();
        await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro });
        await Assert.That(session.Parts.Contains(First)).IsFalse();
        await Assert.That(session.IsStarted).IsTrue();
        await Assert.That(session.IsCanceled).IsFalse();
    }

    [Test]
    public async Task Leave_TakesThePartAwayAndTheMaestroEndsIt()
    {
        var session = NewSession();
        session.Invite(First);
        session.Accept(First);
        session.PartReady(Maestro);
        session.PartReady(First);

        await Assert.That(session.Leave(First)).IsTrue();
        await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro });
        await Assert.That(session.AllPartsReady).IsTrue(); // the remaining member is ready
        await Assert.That(session.Parts.Contains(First)).IsFalse();

        await Assert.That(session.Leave(Maestro)).IsTrue();
        await Assert.That(session.IsCanceled).IsTrue();
        await Assert.That(session.IsOpen).IsFalse();
    }

    [Test]
    public async Task Participants_NamesEveryoneOnce()
    {
        var session = NewSession();
        session.Invite(First);
        session.Invite(Second);
        session.Accept(First);

        var participants = session.Participants();

        await Assert.That(participants.Count).IsEqualTo(3); // maestro, member, still-invited
        await Assert.That(participants.Distinct().Count()).IsEqualTo(3);
        await Assert.That(session.Involves(Second)).IsTrue();
        await Assert.That(session.Involves(0x999)).IsFalse();
    }
}
