using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

/// <summary>
/// Gallery admission: the crime file goes to the onlookers standing in the courtroom, so the request
/// has to name a place in that room - coordinates alone are not a place, because an instance has the
/// same ones.
/// </summary>
[NotInParallel] // the trial manager is a process-wide singleton
public sealed class TrialAudienceAdmissionTests
{
    private const ulong TrialId = 5150;

    private readonly TrialManager _manager = TrialManager.Instance;
    private readonly Dictionary<ulong, Trial> _trials;
    private readonly List<byte[]> _sentPackets = [];
    private readonly Character _onlooker;

    public TrialAudienceAdmissionTests()
    {
        _trials = (Dictionary<ulong, Trial>)typeof(TrialManager)
            .GetField("_trials", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_manager)!;

        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        _onlooker = new Character(new UnitCustomModelParams()) { Id = 88, Name = "Onlooker" };
        _onlooker.Connection = new GameConnection(session.Object);
    }

    [After(Test)]
    public void TearDown() => _trials.Remove(TrialId);

    [Test]
    public async Task SameCoordinatesInsideAnInstance_AreNotTheCourtroom()
    {
        AddCourtTrial(TrialState.Testimony);
        PlaceAtCourt(instanceId: 5);

        _manager.JoinAudience(_onlooker, TrialManager.PlayerTrialType);

        // Refused: the single answer is the error, and no part of the case went out with it.
        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        await Assert.That(_onlooker.Transform.InstanceId).IsEqualTo(5u);
    }

    private Trial AddCourtTrial(TrialState state)
    {
        var trial = new Trial
        {
            Id = TrialId,
            Court = TrialSeatRules.CourtForNation(true),
            DefendantId = 9001,
            DefendantName = "Defendant",
            State = state
        };
        _trials[trial.Id] = trial;
        return trial;
    }

    private void PlaceAtCourt(uint instanceId)
    {
        var (x, y, z) = ArrestRules.CourtPositionFor(AAEmu.Game.Models.StaticValues.FactionsEnum.NuiaAlliance);
        _onlooker.Transform = new Transform(_onlooker, null, x, y, z);

        // The instance id is a cache whose property setter re-resolves the world through WorldManager, a
        // process-wide singleton a unit test has no business publishing, so it is set behind the setter.
        typeof(Transform)
            .GetField("_instanceId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_onlooker.Transform, instanceId);
    }
}
