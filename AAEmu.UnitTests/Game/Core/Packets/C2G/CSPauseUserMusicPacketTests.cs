using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public class CSPauseUserMusicPacketTests
{
    private const uint Maestro = 0x301;
    private const uint Member = 0x302;

    [Test]
    public async Task Read_IsSideEffectFree_ExecuteEndsTheStartedEnsembleMembership()
    {
        var manager = CreateManager();
        var session = StartedSession();
        Ensembles(manager)[Maestro] = session;

        var world = EmptyWorldManager();
        var previousMusic = SwapSingleton(manager);
        var previousWorld = SwapSingleton(world);
        try
        {
            var character = new CharacterMock
            {
                Id = Member,
                ObjId = Member,
                Name = "Member",
                Buffs = null,
            };
            var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = character };
            var packet = new CSPauseUserMusicPacket { Connection = connection };

            packet.Read(new PacketStream());
            await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro, Member });
            await Assert.That(session.IsStarted).IsTrue();

            packet.Execute();
            await Assert.That(session.Members).IsEquivalentTo(new[] { Maestro });
            await Assert.That(session.IsStarted).IsTrue();
            await Assert.That(session.IsCanceled).IsFalse();
        }
        finally
        {
            RestoreSingleton<MusicManager>(previousMusic);
            RestoreSingleton<WorldManager>(previousWorld);
        }
    }

    private static MusicManager CreateManager() =>
        new(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);

    private static EnsembleSession StartedSession()
    {
        var session = new EnsembleSession(Maestro, "Maestro");
        session.Invite(Member);
        session.Accept(Member);
        session.PartReady(Maestro);
        session.PartReady(Member);
        session.Start();
        return session;
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
