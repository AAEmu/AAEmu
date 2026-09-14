using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Network;

public class GameNetworkSocialPacketRegistrationTests
{
    private static readonly Regex SocialPacketName = new("^CS.*(Family|Expedition|Expd).*Packet$", RegexOptions.Compiled);

    [Test]
    public async Task EveryFamilyAndExpeditionRequest_IsRoutedToItsHandler()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;
        var levelOne = packets[1];

        var socialPackets = typeof(CSFamilyChangeTitlePacket).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(CSFamilyChangeTitlePacket).Namespace)
            .Where(type => !type.IsAbstract && typeof(GamePacket).IsAssignableFrom(type))
            .Where(type => SocialPacketName.IsMatch(type.Name))
            .OrderBy(type => type.Name)
            .ToList();
        await Assert.That(socialPackets.Count).IsGreaterThan(40);

        var missing = new List<string>();
        foreach (var packetType in socialPackets)
        {
            var opcodeField = typeof(CSOffsets).GetField(packetType.Name, BindingFlags.Public | BindingFlags.Static);
            if (opcodeField == null)
            {
                missing.Add($"{packetType.Name}: no CSOffsets constant");
                continue;
            }

            var opcode = Convert.ToUInt32(opcodeField.GetValue(null));
            if (!levelOne.TryGetValue(opcode, out var registeredType))
                missing.Add($"{packetType.Name}: opcode 0x{opcode:X3} not registered");
            else if (registeredType != packetType)
                missing.Add($"{packetType.Name}: opcode 0x{opcode:X3} routed to {registeredType.Name}");
        }

        await Assert.That(missing).IsEmpty();
    }
}
