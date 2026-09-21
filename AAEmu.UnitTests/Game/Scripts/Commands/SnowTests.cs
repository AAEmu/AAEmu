using System.Net;
using System.Net.Sockets;
using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using AAEmu.UnitTests.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

[NotInParallel]
public class SnowTests
{
    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => sessionId;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    [Test]
    public async Task CommandBroadcastsChangesAndJoinReplaysLatestSnowState()
    {
        var manager = new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));
        var onlineSession = new RecordingSession(1);
        var onlineCharacter = CreateCharacter(1, onlineSession);
        manager.TryAddCharacter(onlineCharacter);

        var singleton = typeof(Singleton<WorldManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, manager);

        try
        {
            var command = new Snow();
            command.Execute(onlineCharacter, [bool.TrueString], null!);

            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[0], true);

            var joiningWhileSnowingSession = new RecordingSession(2);
            var joiningWhileSnowing = CreateCharacter(2, joiningWhileSnowingSession);
            manager.OnPlayerJoin(joiningWhileSnowing);

            await Assert.That(joiningWhileSnowingSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joiningWhileSnowingSession.Packets[0], true);

            command.Execute(onlineCharacter, [bool.FalseString], null!);

            await Assert.That(manager.IsSnowing).IsFalse();
            await Assert.That(onlineSession.Packets.Count).IsEqualTo(2);
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[1], false);

            var joiningSession = new RecordingSession(3);
            var joiningCharacter = CreateCharacter(3, joiningSession);
            manager.OnPlayerJoin(joiningCharacter);

            await Assert.That(joiningSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joiningSession.Packets[0], false);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }

    private static Character CreateCharacter(uint id, ISession session)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = $"Player{id}" };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        return character;
    }
}
