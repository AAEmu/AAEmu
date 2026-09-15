using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

/// <summary>
/// A jury invitation or a promised chair that outlives the gathering phase must not seat anyone: the
/// case file and the vote go with the seat, and the client keeps both dialogs on screen until it
/// answers them. The replies here arrive after the phase moved on.
/// </summary>
[NotInParallel] // the trial manager is a process-wide singleton
public sealed class TrialJuryCallGateTests
{
    private const ulong TrialId = 4242;

    private readonly TrialManager _manager = TrialManager.Instance;
    private readonly Dictionary<ulong, Trial> _trials;
    private readonly List<byte[]> _sentPackets = [];
    private readonly Character _juror;

    public TrialJuryCallGateTests()
    {
        _trials = (Dictionary<ulong, Trial>)typeof(TrialManager)
            .GetField("_trials", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_manager)!;

        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        var connection = new GameConnection(session.Object);
        _juror = new Character(new UnitCustomModelParams()) { Id = 77, Name = "Juror" };
        _juror.Connection = connection;
        connection.ActiveChar = _juror;
    }

    [After(Test)]
    public void TearDown() => _trials.Remove(TrialId);

    [Test]
    public async Task InviteAcceptedAfterTheGatheringWindow_IsRefusedAndDropped()
    {
        var trial = AddTrial(TrialState.Testimony);
        trial.Invited.Add(_juror.Id);

        _manager.OnReplyInvite(_juror, accept: true, trial.Id);

        await Assert.That(trial.Summoned).IsEmpty();
        await Assert.That(trial.Invited).DoesNotContain(_juror.Id);
        await Assert.That(trial.Jurors).IsEmpty();
    }

    [Test]
    public async Task SummonsAnsweredAfterTheGatheringWindow_DoesNotSeat()
    {
        var trial = AddTrial(TrialState.Sentence);
        trial.Summoned[_juror.Id] = 1;

        var seated = _manager.SeatJuror(_juror, trial.Id, 1);

        await Assert.That(seated).IsFalse();
        await Assert.That(trial.Jurors).IsEmpty();
        await Assert.That(trial.Summoned).DoesNotContainKey(_juror.Id);
    }

    [Test]
    public async Task DeclineForACaseThatIsGone_IsIgnored()
    {
        // The case closed while the invite dialog was still on screen: this used to read the phase
        // off a null trial and throw.
        _manager.OnReplyInvite(_juror, accept: false, trialId: TrialId);

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AcceptForACaseThatIsGone_IsIgnored()
    {
        _manager.OnReplyInvite(_juror, accept: true, trialId: TrialId);

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    private Trial AddTrial(TrialState state)
    {
        var trial = new Trial
        {
            Id = TrialId,
            Court = 0,
            DefendantId = 9001,
            DefendantName = "Defendant",
            State = state
        };
        _trials[trial.Id] = trial;
        return trial;
    }
}
