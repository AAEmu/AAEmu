using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;

namespace AAEmu.UnitTests.Game.Core.Network;

/// <summary>
/// The content-roster delete and survey-form reply requests are only "handled" if their opcodes
/// are actually routed in <c>GameNetwork</c>. This pins the routing, so a dropped
/// <c>RegisterPacket</c> line turns a silent no-op into a failing test.
/// </summary>
public class GameNetworkRosterSurveyPacketRegistrationTests
{
    private static ConcurrentDictionary<uint, Type> LevelOnePackets()
    {
        var handler = (GameProtocolHandler)typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(GameNetwork.Instance)!;
        var packets = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)typeof(GameProtocolHandler)
            .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handler)!;
        return packets[1];
    }

    [Test]
    public async Task RosterDeleteAndSurveyReply_AreRoutedToTheirHandlers()
    {
        var levelOne = LevelOnePackets();
        var expected = new (ushort Opcode, Type Type)[]
        {
            (CSOffsets.CSContentRosterDeletePacket, typeof(CSContentRosterDeletePacket)),
            (CSOffsets.CSSurveyFormReplyPacket, typeof(CSSurveyFormReplyPacket)),
        };

        var problems = new List<string>();
        foreach (var (opcode, type) in expected)
        {
            if (!levelOne.TryGetValue(opcode, out var registered))
                problems.Add($"opcode 0x{opcode:X3} ({type.Name}) is not registered");
            else if (registered != type)
                problems.Add($"opcode 0x{opcode:X3} is routed to {registered.Name}, not {type.Name}");
        }

        await Assert.That(problems).IsEmpty();
    }

    /// <summary>
    /// The roster and survey request block occupies a contiguous opcode run; a collision inside it
    /// would silently shadow one of the two handlers.
    /// </summary>
    [Test]
    public async Task RosterAndSurveyOpcodes_AreDistinct()
    {
        var opcodes = new (string Name, ushort Opcode)[]
        {
            ("CSContentRosterDeletePacket", CSOffsets.CSContentRosterDeletePacket),
            ("CSContentRosterSavePacket", CSOffsets.CSContentRosterSavePacket),
            ("CSContentRosterMemberListPacket", CSOffsets.CSContentRosterMemberListPacket),
            ("CSSurveyFormReplyPacket", CSOffsets.CSSurveyFormReplyPacket),
        };

        var duplicates = opcodes
            .GroupBy(entry => entry.Opcode)
            .Where(group => group.Count() > 1)
            .Select(group => $"0x{group.Key:X3}: {string.Join(", ", group.Select(entry => entry.Name))}")
            .ToList();

        await Assert.That(duplicates).IsEmpty();
    }

    /// <summary>
    /// The two requests must not collide with anything else already routed on the same level, or
    /// whichever registration won would answer for the other.
    /// </summary>
    [Test]
    public async Task RosterDeleteAndSurveyReply_AreRoutedToDistinctOpcodes()
    {
        var levelOne = LevelOnePackets();
        var deleteOpcode = CSOffsets.CSContentRosterDeletePacket;
        var replyOpcode = CSOffsets.CSSurveyFormReplyPacket;

        await Assert.That(deleteOpcode).IsNotEqualTo(replyOpcode);
        await Assert.That(levelOne[deleteOpcode]).IsNotEqualTo(levelOne[replyOpcode]);
    }
}
