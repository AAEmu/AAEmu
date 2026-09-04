using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network.Core;
using AAEmu.World.Core.Network;
using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.World.Core.Zone;

public class ZoneConnectionRegistryTests
{
    private sealed class StubSession(uint sessionId) : ISession
    {
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId { get; } = sessionId;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) { }
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    private static ZoneConnection Loaded(uint sessionId, uint zoneId, uint instanceId)
    {
        var connection = new ZoneConnection(new StubSession(sessionId))
        {
            ZoneId = zoneId,
            InstanceId = instanceId,
            State = ZoneConnectionState.ZoneLoaded
        };
        return connection;
    }

    [Test]
    public async Task TwoCopiesOfSameZone_StayRegistered()
    {
        var registry = new ZoneConnectionRegistry();
        var first = Loaded(1, 265, 7);
        var second = Loaded(2, 265, 8);
        registry.Add(first);
        registry.Add(second);
        registry.Index(first);
        registry.Index(second);

        await Assert.That(registry.GetLoaded(265, 7)?.Id).IsEqualTo(1u);
        await Assert.That(registry.GetLoaded(265, 8)?.Id).IsEqualTo(2u);
        await Assert.That(registry.LoadedCount).IsEqualTo(2);
    }

    [Test]
    public async Task DuplicateZoneAndInstance_ReplacesPrevious()
    {
        var registry = new ZoneConnectionRegistry();
        var first = Loaded(1, 265, 7);
        var second = Loaded(2, 265, 7);
        registry.Add(first);
        registry.Index(first);
        registry.Add(second);
        registry.Index(second);

        await Assert.That(registry.GetLoaded(265, 7)?.Id).IsEqualTo(2u);
        await Assert.That(registry.LoadedCount).IsEqualTo(1);
    }

    [Test]
    public async Task UniqueLoaded_OneCopy_ReturnsItEvenWhenInstanceIdNonZero()
    {
        var registry = new ZoneConnectionRegistry();
        var only = Loaded(1, 265, 7);
        registry.Add(only);
        registry.Index(only);

        await Assert.That(registry.GetUniqueLoaded(265)?.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task UniqueLoaded_TwoCopies_DoesNotPickOne()
    {
        var registry = new ZoneConnectionRegistry();
        var first = Loaded(1, 265, 7);
        var second = Loaded(2, 265, 8);
        registry.Add(first);
        registry.Add(second);
        registry.Index(first);
        registry.Index(second);

        await Assert.That(registry.GetUniqueLoaded(265)).IsNull();
    }

    [Test]
    public async Task UniqueLoaded_TwoCopies_PrefersInstanceZero()
    {
        var registry = new ZoneConnectionRegistry();
        var continent = Loaded(1, 184, 0);
        var extra = Loaded(2, 184, 3);
        registry.Add(continent);
        registry.Add(extra);
        registry.Index(continent);
        registry.Index(extra);

        await Assert.That(registry.GetUniqueLoaded(184)?.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task MissingCopy_ReturnsNull()
    {
        var registry = new ZoneConnectionRegistry();
        await Assert.That(registry.GetLoaded(265, 7)).IsNull();
        await Assert.That(registry.GetUniqueLoaded(265)).IsNull();
    }

    [Test]
    public async Task Remove_DropsOnlyThatCopy()
    {
        var registry = new ZoneConnectionRegistry();
        var first = Loaded(1, 265, 7);
        var second = Loaded(2, 265, 8);
        registry.Add(first);
        registry.Add(second);
        registry.Index(first);
        registry.Index(second);

        registry.Remove(1);

        await Assert.That(registry.GetLoaded(265, 7)).IsNull();
        await Assert.That(registry.GetLoaded(265, 8)?.Id).IsEqualTo(2u);
    }
}
