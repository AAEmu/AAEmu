using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class SquadLoginTests
{
    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    [Test]
    public async Task LoginWithoutSquad_ClearsQueueWithoutAnnouncingADisband_OnRepeatedLogin()
    {
        var session = new RecordingSession();
        var character = new Character(new AAEmu.Game.Models.Game.Units.UnitCustomModelParams()) { Id = 1007, Name = "Test" };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        var manager = new SquadManager();

        manager.SyncClientSquadAfterLogin(character);
        manager.SyncClientSquadAfterLogin(character);

        await Assert.That(session.Packets.Count).IsEqualTo(2);
        foreach (var packet in session.Packets)
        {
            await Assert.That(BitConverter.ToUInt16(packet, 6)).IsEqualTo(SCOffsets.SCCancelInstantGamePacket);
            await Assert.That(BitConverter.ToUInt16(packet, 8)).IsEqualTo((ushort)0);
        }
    }

    [Test]
    public async Task LoginWithSquad_PreservesSquadAndRealDisbandStillNotifies()
    {
        var session = new RecordingSession();
        var character = new Character(new AAEmu.Game.Models.Game.Units.UnitCustomModelParams())
            { Id = 1007, ObjId = 1007, Name = "Test" };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        var manager = new SquadManager();
        var squad = new AAEmu.Game.Models.Game.Squad.Squad { Id = 1, LeaderCharacterId = character.Id };
        squad.Members.Add(new AAEmu.Game.Models.Game.Squad.SquadMember { CharacterId = character.Id, IsLeader = true });
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var squads = (Dictionary<uint, AAEmu.Game.Models.Game.Squad.Squad>)typeof(SquadManager)
            .GetField("_squads", fields)!.GetValue(manager)!;
        var membership = (Dictionary<uint, uint>)typeof(SquadManager)
            .GetField("_characterSquad", fields)!.GetValue(manager)!;
        squads.Add(squad.Id, squad);
        membership.Add(character.Id, squad.Id);
        var world = new AAEmu.Game.Core.Managers.World.WorldManager(null, null, null, null, null);
        var characters = (System.Collections.Concurrent.ConcurrentDictionary<uint, Character>)world.GetType()
            .GetField("_characters", fields)!.GetValue(world)!;
        characters[character.ObjId] = character;
        var singleton = typeof(AAEmu.Commons.Utils.Singleton<AAEmu.Game.Core.Managers.World.WorldManager>)
            .GetField("s_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previous = singleton.GetValue(null);
        singleton.SetValue(null, world);
        try
        {
            manager.SyncClientSquadAfterLogin(character);
            await Assert.That(session.Packets).IsEmpty();
            await Assert.That(membership[character.Id]).IsEqualTo(squad.Id);
            manager.Disband(character);
            await Assert.That(squads).IsEmpty();
            await Assert.That(membership).IsEmpty();
            await Assert.That(session.Packets.Count).IsEqualTo(1);
            await Assert.That(BitConverter.ToUInt16(session.Packets[0], 6)).IsEqualTo(SCOffsets.SCDisbandSquadPacket);
            session.Packets.Clear();
            manager.SyncClientSquadAfterLogin(character);
            await Assert.That(session.Packets.Count).IsEqualTo(1);
            await Assert.That(BitConverter.ToUInt16(session.Packets[0], 6)).IsEqualTo(SCOffsets.SCCancelInstantGamePacket);
        }
        finally
        {
            singleton.SetValue(null, previous);
        }
    }
}
