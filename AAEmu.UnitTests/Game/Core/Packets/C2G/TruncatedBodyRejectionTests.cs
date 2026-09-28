﻿﻿using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The client-to-game gate for a body the client cut short. Most C2G handlers write state from
/// inside <c>Read</c>, so the read has to stop at the byte that ran past the end: a check after
/// <c>Read</c> returns would be after the write.
/// </summary>
/// <remarks>
/// Every case asserts <c>ExecuteRan</c> rather than only that an exception came out. An
/// <c>Assert.Throws</c> on its own also passes when the handler ran to completion and the throw came
/// from somewhere later, so it cannot tell "refused" from "acted, then blew up".
/// </remarks>
[NotInParallel]
public class TruncatedBodyRejectionTests
{
    // A synthetic opcode: these probes are registered by this fixture alone and are never reached
    // through the real client map, so any value works as long as nothing else claims it.
    public const ushort ProbeOpcode = 0xFFF;

    /// <summary>
    /// A handler shaped like the real ones: capture in <c>Read</c>, act in <c>Execute</c>. The string
    /// is the whole body, so a body that ends inside it is exactly the truncation case.
    /// </summary>
    public class TruncatedBodyProbePacket() : GamePacket(ProbeOpcode, 1)
    {
        /// <summary>The instance the dispatch built, for the cases that go through OnReceive.</summary>
        public static TruncatedBodyProbePacket LastDispatch { get; set; }

        public string Captured { get; private set; } = string.Empty;
        public bool ExecuteRan { get; private set; }

        public override void Read(PacketStream stream)
        {
            LastDispatch = this;
            Captured = stream.ReadString();
        }

        public override void Execute() => ExecuteRan = true;
    }

    /// <summary>
    /// The same packet declaring that a short body is part of its contract. Nothing in the C2G or
    /// proxy set needs this today; the case exists so the opt-out is a covered branch rather than an
    /// untested switch.
    /// </summary>
    public sealed class TruncationTolerantProbePacket() : TruncatedBodyProbePacket
    {
        protected override bool TolerateTruncatedBody => true;
    }

    [Before(Test)]
    public void ResetProbes() => TruncatedBodyProbePacket.LastDispatch = null;

    [Test]
    public async Task CompleteBody_ReachesExecute()
    {
        var packet = Probe();

        // The negative control for every case below: with a whole body the handler acts. Without it,
        // a fixture that never reaches the packet at all would pass on ExecuteRan == false.
        var threw = Try(() => Dispatch(packet, Body("complete")));

        await Assert.That(threw).IsFalse();
        await Assert.That(packet.ExecuteRan).IsTrue();
        await Assert.That(packet.Captured).IsEqualTo("complete");
    }

    [Test]
    public async Task EmptyBody_IsRefusedBeforeExecute()
    {
        var packet = Probe();

        var threw = Try(() => Dispatch(packet, new PacketStream()));

        await Assert.That(threw).IsTrue();
        await Assert.That(packet.ExecuteRan).IsFalse();
    }

    [Test]
    public async Task PayloadCutInHalf_IsRefusedBeforeExecute()
    {
        var packet = Probe();

        // A well formed prefix that promises eight bytes, with four of them present.
        var truncated = new PacketStream()
            .Write((ushort)8)
            .Write("half"u8.ToArray());

        var threw = Try(() => Dispatch(packet, truncated));

        await Assert.That(threw).IsTrue();
        await Assert.That(packet.ExecuteRan).IsFalse();
    }

    [Test]
    public async Task PrefixDeclaresFiveBytesWithTwoPresent_IsRefusedBeforeExecute()
    {
        var packet = Probe();

        var truncated = new PacketStream()
            .Write((ushort)5)
            .Write("ab"u8.ToArray());

        var threw = Try(() => Dispatch(packet, truncated));

        await Assert.That(threw).IsTrue();
        await Assert.That(packet.ExecuteRan).IsFalse();
        // The read stopped at the declared length, so nothing was captured either.
        await Assert.That(packet.Captured).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task OptedOutPacket_ReachesExecuteOnATruncatedBody()
    {
        var packet = new TruncationTolerantProbePacket { Connection = Connection() };

        var threw = Try(() => Dispatch(packet, new PacketStream()));

        await Assert.That(threw).IsFalse();
        await Assert.That(packet.ExecuteRan).IsTrue();
        // The lenient stream still records the overrun; only the answer differs.
        await Assert.That(packet.Captured).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Dispatch_DeliversACompleteBodyToExecute()
    {
        OnReceive(Frame(Body("complete")));

        var dispatched = TruncatedBodyProbePacket.LastDispatch;
        await Assert.That(dispatched).IsNotNull();
        await Assert.That(dispatched!.ExecuteRan).IsTrue();
        await Assert.That(dispatched.Captured).IsEqualTo("complete");
    }

    /// <summary>
    /// The same refusal through the real <see cref="GameProtocolHandler"/> dispatch. This is the case
    /// that pins the one line in the handler: the probes above arm the stream themselves, so only
    /// this one fails if the dispatch stops arming it.
    /// </summary>
    [Test]
    public async Task Dispatch_RefusesATruncatedBodyBeforeExecute()
    {
        var truncated = new PacketStream()
            .Write((ushort)8)
            .Write("half"u8.ToArray());

        OnReceive(Frame(truncated));

        var dispatched = TruncatedBodyProbePacket.LastDispatch;
        await Assert.That(dispatched).IsNotNull();
        await Assert.That(dispatched!.ExecuteRan).IsFalse();
    }

    /// <summary>
    /// Runs one packet through the arming the dispatch performs, then through Decode. Decode is the
    /// entry point the defect lives in: Read() on its own never reaches Execute().
    /// </summary>
    private static void Dispatch(GamePacket packet, PacketStream body)
    {
        body.RequireComplete(packet.RequiresCompleteBody);
        packet.Decode(body);
    }

    /// <summary>
    /// Drives a level-1 client frame through the handler's own registration table, so the arming
    /// under test is the handler's and not this file's.
    /// </summary>
    private static void OnReceive(PacketStream frame)
    {
        var handler = new GameProtocolHandler();
        handler.RegisterPacket(ProbeOpcode, 1, typeof(TruncatedBodyProbePacket));
        var bytes = frame.GetBytes();
        handler.OnReceive(Connection(), bytes, 0, bytes.Length);
    }

    /// <summary>[u16 payloadLen][unk][level 1][hash][counter][u16 opcode][body]</summary>
    private static PacketStream Frame(PacketStream body)
    {
        var payloadLen = 6 + body.Count; // unk + level + hash + counter + u16 opcode
        return new PacketStream()
            .Write((ushort)payloadLen)
            .Write((byte)0)   // unk
            .Write((byte)1)   // level
            .Write((byte)0)   // hash
            .Write((byte)0)   // counter
            .Write(ProbeOpcode)
            .Write(body, appendSize: false); // the body is already framed; no second prefix
    }

    private static PacketStream Body(string text) =>
        new PacketStream().Write(text, appendSize: true);

    private static TruncatedBodyProbePacket Probe() => new() { Connection = Connection() };

    private static GameConnection Connection() =>
        new(Mock.Of<ISession>().Object)
        {
            ActiveChar = new CharacterMock { Id = 1, Name = "Tester" },
        };

    private static bool Try(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
