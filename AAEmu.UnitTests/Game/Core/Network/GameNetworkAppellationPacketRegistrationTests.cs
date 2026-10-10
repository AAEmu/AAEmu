using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Network;

public class GameNetworkAppellationPacketRegistrationTests
{
    [Test]
    [Arguments(typeof(CSChangeAppellationPacket), nameof(CSOffsets.CSChangeAppellationPacket))]
    [Arguments(typeof(CSSetAppellationStampPacket), nameof(CSOffsets.CSSetAppellationStampPacket))]
    public async Task AppellationRequest_IsRoutedToItsHandler(Type packetType, string offsetName)
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;
        var opcode = Convert.ToUInt32(typeof(CSOffsets).GetField(offsetName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null));

        await Assert.That(packets[1].TryGetValue(opcode, out var registeredType)).IsTrue();
        await Assert.That(registeredType).IsEqualTo(packetType);
    }
}
