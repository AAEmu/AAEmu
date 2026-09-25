using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Network;

public class GameNetworkEnsemblePacketRegistrationTests
{
    [Test]
    public async Task EnsembleMidiBinReady_IsRoutedToItsHandler()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)
            typeof(GameProtocolHandler)
                .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(handler)!;

        var levelOne = packets[1];
        await Assert.That(levelOne.TryGetValue(CSOffsets.CSEnsembleMidiBinReadyPacket, out var registered))
            .IsTrue();
        await Assert.That(registered).IsEqualTo(typeof(CSEnsembleMidiBinReadyPacket));
    }
}
