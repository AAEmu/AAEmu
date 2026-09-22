using System.Collections.Concurrent;
using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class TrialChatAuthorizationTests
{
    [Test]
    public async Task TrialRoute_DeliversOnlyToCurrentDefendantAndSeatedJurors()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var manager = TrialManager.Instance;
        var trials = (Dictionary<ulong, Trial>)typeof(TrialManager).GetField("_trials", fields)!.GetValue(manager)!;
        var courtTrials = (ulong?[])typeof(TrialManager).GetField("_courtTrial", fields)!.GetValue(manager)!;
        var previousCourt = courtTrials[0];
        var world = new WorldManager(null, null, null, null, null);
        var characters = (ConcurrentDictionary<uint, Character>)typeof(WorldManager)
            .GetField("_characters", fields)!.GetValue(world)!;
        var singleton = typeof(Singleton<WorldManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousWorld = singleton.GetValue(null);
        const ulong trialId = 91007;

        var defendant = OnlineCharacter(101);
        var juror = OnlineCharacter(102);
        var invited = OnlineCharacter(103);
        var trial = new Trial { Id = trialId, Court = 0, DefendantId = defendant.Id };
        trial.Jurors.Add(new TrialJuror { CharacterId = juror.Id });
        trial.Invited.Add(invited.Id);

        try
        {
            trials[trialId] = trial;
            courtTrials[0] = trialId;
            characters[defendant.Id] = defendant;
            characters[juror.Id] = juror;
            characters[invited.Id] = invited;
            singleton.SetValue(null, world);

            await Assert.That(manager.SendChatMessage(defendant, "court")).IsEqualTo(2);
            await Assert.That(manager.SendChatMessage(invited, "spoofed")).IsEqualTo(0);

            courtTrials[0] = null;
            await Assert.That(manager.SendChatMessage(defendant, "queued")).IsEqualTo(0);
        }
        finally
        {
            trials.Remove(trialId);
            courtTrials[0] = previousCourt;
            singleton.SetValue(null, previousWorld);
        }
    }

    private static Character OnlineCharacter(uint id)
    {
        var session = Mock.Of<ISession>();
        var character = new Character(new UnitCustomModelParams()) { Id = id, Name = $"Character {id}" };
        character.Connection = new GameConnection(session.Object) { ActiveChar = character };
        typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, true);
        return character;
    }
}
