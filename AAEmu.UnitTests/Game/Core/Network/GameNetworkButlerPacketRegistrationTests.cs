using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Network;

public class GameNetworkButlerPacketRegistrationTests
{
    [Test]
    public async Task SupportedButlerRequests_AreRoutedToTheirHandlers()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;

        var levelOne = packets[1];
        var expected = new Dictionary<uint, Type>
        {
            [CSOffsets.CSUnbindButlerPacket] = typeof(CSUnbindButlerPacket),
            [CSOffsets.CSSwapButlerItemPacket] = typeof(CSSwapButlerItemPacket),
            [CSOffsets.CSExpandButlerUsableSlotPacket] = typeof(CSExpandButlerUsableSlotPacket),
            [CSOffsets.CSRequestButlerHarvestJobPacket] = typeof(CSRequestButlerHarvestJobPacket),
            [CSOffsets.CSChargeButlerWorldResourcePacket] = typeof(CSChargeButlerWorldResourcePacket),
            [CSOffsets.CSChangeButlerNamePacket] = typeof(CSChangeButlerNamePacket)
        };

        foreach (var (opcode, handlerType) in expected)
        {
            await Assert.That(levelOne.TryGetValue(opcode, out var registeredType)).IsTrue();
            await Assert.That(registeredType).IsEqualTo(handlerType);
        }
    }
}
