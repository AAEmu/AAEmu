using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class EnterWorldManagerEnsembleLogoutTests
{
    private const uint Maestro = 0x501;
    private const uint Member = 0x502;

    [Test]
    public async Task OrderlyLogout_ReleasesAStartedEnsembleBeforeWorldRemoval()
    {
        var music = new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);
        var session = new EnsembleSession(Maestro, "Maestro");
        session.Invite(Member);
        session.Accept(Member);
        session.PartReady(Maestro);
        session.PartReady(Member);
        session.Start();
        Ensembles(music)[Maestro] = session;

        var previousMusic = SwapSingleton(music);
        var previousWorld = SwapSingleton(EmptyWorldManager());
        try
        {
            var enterWorld = new EnterWorldManager(
                Mock.Of<IAccountManager>().Object,
                Mock.Of<IStreamManager>().Object,
                Mock.Of<IQuestManager>().Object,
                Mock.Of<ITeamManager>().Object,
                Mock.Of<IChatManager>().Object,
                Mock.Of<IFamilyManager>().Object,
                Mock.Of<IWorldManager>().Object);
            var character = new CharacterMock { Id = Member, ObjId = Member, Name = "Member" };

            enterWorld.ReleaseEnsembleBeforeWorldRemoval(character);

            await Assert.That(Ensembles(music)).IsEmpty();
            await Assert.That(session.IsCanceled).IsTrue();
        }
        finally
        {
            RestoreSingleton<WorldManager>(previousWorld);
            RestoreSingleton<MusicManager>(previousMusic);
        }
    }

    private static Dictionary<uint, EnsembleSession> Ensembles(MusicManager manager) =>
        (Dictionary<uint, EnsembleSession>)typeof(MusicManager)
            .GetField("_ensembles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;

    private static WorldManager EmptyWorldManager() => new(
        Mock.Of<ITickManager>().Object,
        Mock.Of<IWorldIdManager>().Object,
        new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
        new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
        new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

    private static object SwapSingleton<T>(T replacement) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, replacement);
        return previous;
    }

    private static void RestoreSingleton<T>(object previous) where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, previous);
}
