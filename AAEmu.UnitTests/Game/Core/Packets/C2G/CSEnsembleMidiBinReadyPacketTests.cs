using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public class CSEnsembleMidiBinReadyPacketTests
{
    private const uint Maestro = 0x601;
    private const uint Member = 0x602;

    [Test]
    public async Task NegativeOrEmptyStringLength_IsRejectedBeforePartMutation()
    {
        var manager = new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);
        var session = OpenSession();
        Ensembles(manager)[Maestro] = session;
        var world = EmptyWorldManager();
        var characters = (ConcurrentDictionary<uint, Character>)typeof(WorldManager)
            .GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world)!;
        var maestro = NewCharacter(Maestro, "Maestro");
        var member = NewCharacter(Member, "Member");
        characters[Maestro] = maestro;
        characters[Member] = member;

        var previousMusic = SwapSingleton(manager);
        var previousWorld = SwapSingleton(world);
        try
        {
            foreach (var dataLength in new short[] { 0, -1 })
            {
                var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = member };
                var packet = new CSEnsembleMidiBinReadyPacket { Connection = connection };
                packet.Read(Body(dataLength));

                await Assert.That(packet.Data).IsEqualTo(string.Empty);
                packet.Execute();
                await Assert.That(session.Parts).IsEmpty();
            }
        }
        finally
        {
            RestoreSingleton<WorldManager>(previousWorld);
            RestoreSingleton<MusicManager>(previousMusic);
        }
    }

    private static CharacterMock NewCharacter(uint id, string name) => new()
    {
        Id = id,
        ObjId = id,
        Name = name,
        Buffs = null,
    };

    private static EnsembleSession OpenSession()
    {
        var session = new EnsembleSession(Maestro, "Maestro");
        session.Invite(Member);
        session.Accept(Member);
        return session;
    }

    private static PacketStream Body(short dataLength) =>
        new PacketStream()
            .WriteBc(Member)
            .WriteBc(Maestro)
            .Write(0u)
            .Write(dataLength);

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
