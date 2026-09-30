using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;
using AAEmu.World.Core.Packets.Zw;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// The zone reports its npc abuser lists with a register, a batched unregister and a clear.
/// These cover the wire shape of all three, the World-side aggro rows the two removal events have
/// to drop, and the removal paths the events do not cover: an npc leaving the world and an abuser
/// leaving the world. Without those, a row survives both of its participants and answers later
/// queries with a unit nothing owns.
/// </summary>
[NotInParallel]
public class NpcAbuserRegistryTests
{
    private const uint Npc = 0x010203;
    private const uint Abuser = 0x0A0B0C;

    private IDisposable _worldManager = null!;
    private WorldInstance _world = null!;

    [Before(Test)]
    public void ResetRegistry()
    {
        NpcAbuserRegistry.Reset();
        // The two removal events reach for the npc's World-side aggro table, which is looked up
        // across the live worlds; without a manager installed that lookup cannot even resolve.
        _worldManager = TestDungeonWorld.InstallWorldManager();
        _world = TestDungeonWorld.CreateWorld(9002, 0, 1);
    }

    [After(Test)]
    public void ClearRegistry()
    {
        NpcAbuserRegistry.Reset();
        _worldManager.Dispose();
    }

    private static byte[] RegisterBody(uint npcUnitId, uint abuserUnitId)
    {
        var body = new PacketStream();
        body.WriteBc(npcUnitId);
        body.WriteBc(abuserUnitId);
        return body.GetBytes();
    }

    private static byte[] UnregisterBody(uint npcUnitId, params uint[] abuserUnitIds)
    {
        var body = new PacketStream();
        body.WriteBc(npcUnitId);
        body.Write((byte)abuserUnitIds.Length);
        foreach (var id in abuserUnitIds)
            body.WriteBc(id);
        return body.GetBytes();
    }

    private static byte[] ClearBody(uint npcUnitId)
    {
        var body = new PacketStream();
        body.WriteBc(npcUnitId);
        return body.GetBytes();
    }

    private static bool Handle(ushort opcode, byte[] body) =>
        new ZoneSimRelay().TryHandle(opcode, body, body.Length);

    [Test]
    public async Task Register_AddsTheRowTheZoneReported()
    {
        var handled = Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsTrue();
        await Assert.That(NpcAbuserRegistry.GetAbusers(Npc)).IsEquivalentTo(new[] { Abuser });
    }

    [Test]
    public async Task Register_IsIdempotent_BecauseTheZoneResendsLiveRows()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(1);
    }

    [Test]
    public async Task Unregister_DropsOnlyTheListedIds()
    {
        const uint other = 0x0D0E0F;
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, other));

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, Abuser));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsFalse();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, other)).IsTrue();
    }

    [Test]
    public async Task Unregister_CarriesNoCapOfItsOwnOnTheCountByte()
    {
        // The count is a byte, so the parser could hold 255 ids and the test uses all of them to prove
        // it does not - an added cap here would silently drop rows and leave the mirror behind.
        //
        // The native serializer clamps this batch to 100, so 100 is the widest that will actually
        // arrive and 255 is past it. Reading the full byte range is deliberate and harmless: the extra
        // ids are the ones the packet really carries, so accepting them cannot invent a removal, and
        // a packet claiming more ids than its body holds is refused by the length check regardless.
        var ids = Enumerable.Range(1, byte.MaxValue)
            .Select(i => (uint)(0x1000 + i))
            .ToArray();
        foreach (var id in ids)
            Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, id));

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, ids));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.GetAbusers(Npc)).IsEmpty();
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_DropsEveryAbuserOfThatNpcOnly()
    {
        const uint otherNpc = 0x040506;
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(otherNpc, Abuser));

        var handled = Handle(ZwOpcodes.ClearNpcAbusers, ClearBody(Npc));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.GetAbusers(Npc)).IsEmpty();
        await Assert.That(NpcAbuserRegistry.IsRegistered(otherNpc, Abuser)).IsTrue();
    }
    [Test]
    [Arguments(0)]
    [Arguments(3 + 1 + 3)]
    [Arguments(3 + 3 + 3)]
    [Arguments(8)]
    public async Task Register_RejectsAnyBodyThatIsNotExactlyTwoIds(int bodyLen)
    {
        var body = new byte[bodyLen];
        for (var i = 0; i < bodyLen; i++)
            body[i] = 0x11;

        await Assert.That(Handle(ZwOpcodes.RegisterNpcAbuser, body)).IsFalse();
        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(3 + 1 + 3)]
    public async Task Clear_RejectsAnyBodyThatIsNotExactlyOneId(int bodyLen)
    {
        // Exactly three bytes IS one id and is the valid case; every other length is malformed.
        var body = new byte[bodyLen];
        for (var i = 0; i < bodyLen; i++)
            body[i] = 0x22;

        await Assert.That(Handle(ZwOpcodes.ClearNpcAbusers, body)).IsFalse();
    }

    [Test]
    public async Task Clear_AcceptsExactlyOneId()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        var handled = Handle(ZwOpcodes.ClearNpcAbusers, ClearBody(Npc));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsFalse();
    }

    [Test]
    public async Task Unregister_RejectsACountThatOverrunsTheBody()
    {
        // A count larger than the ids actually present would read ids out of unrelated packets.
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        var body = new PacketStream();
        body.WriteBc(Npc);
        body.Write((byte)4); // claims four ids
        body.WriteBc(Abuser); // ... but only one is present
        var raw = body.GetBytes();

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, raw);

        await Assert.That(handled).IsFalse();
        // The rejected packet must leave the row alone rather than half-applying it.
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsTrue();
    }

    [Test]
    public async Task Unregister_RejectsATrailingByteTheCountDoesNotCover()
    {
        // One id more than the count announces: the extra bytes are not part of this event, and
        // reading only the announced count would silently ignore them.
        var body = new PacketStream();
        body.WriteBc(Npc);
        body.Write((byte)2);
        body.WriteBc((uint)0x2001);
        body.WriteBc((uint)0x2002);
        body.WriteBc((uint)0x2003);
        var raw = body.GetBytes();
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, 0x2001));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, 0x2003));

        await Assert.That(Handle(ZwOpcodes.UnregisterNpcAbusers, raw)).IsFalse();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, 0x2001)).IsTrue();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, 0x2003)).IsTrue();
    }

    [Test]
    public async Task Unregister_WithZeroCountIsAValidNoOp()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsTrue();
    }
    [Test]
    public async Task NpcLeavingTheWorld_DropsEveryRowThatNpcHeld()
    {
        const uint otherNpc = 0x070809;
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(otherNpc, Abuser));

        NpcAbuserRegistry.ForgetNpc(Npc);

        await Assert.That(NpcAbuserRegistry.GetAbusers(Npc)).IsEmpty();
        await Assert.That(NpcAbuserRegistry.IsRegistered(otherNpc, Abuser)).IsTrue();
    }

    [Test]
    public async Task AbuserLeavingTheWorld_DropsItFromEveryNpcThatListedIt()
    {
        const uint otherNpc = 0x0A0A0B;
        const uint otherAbuser = 0x0B0B0C;
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(otherNpc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(otherNpc, otherAbuser));

        var removed = NpcAbuserRegistry.ForgetUnit(Abuser);

        await Assert.That(removed).IsEqualTo(2);
        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Abuser)).IsFalse();
        await Assert.That(NpcAbuserRegistry.IsRegistered(otherNpc, Abuser)).IsFalse();
        await Assert.That(NpcAbuserRegistry.IsRegistered(otherNpc, otherAbuser)).IsTrue();
    }

    [Test]
    public async Task AbuserLeavingTheWorld_LeavesNoEmptyNpcRowsBehind()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(1);

        NpcAbuserRegistry.ForgetUnit(Abuser);

        // The npc must not survive as a row that merely holds nothing: that is the leak itself.
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(0);
    }

    [Test]
    public async Task Unregister_ThatEmptiesAnNpcDropsItsRowToo()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, Abuser));

        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
    }

    [Test]
    public async Task AUnitCannotAbuseItself()
    {
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Npc));

        await Assert.That(NpcAbuserRegistry.IsRegistered(Npc, Npc)).IsFalse();
        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(0);
    }

    private Npc MirrorNpc(uint objId)
    {
        var npc = new Npc { ObjId = objId };
        TestDungeonWorld.Attach(npc, _world);
        _world.AddObject(npc);
        return npc;
    }

    /// <summary>
    /// The abuse list must still be readable at the moment the kill is mirrored.
    /// </summary>
    /// <remarks>
    /// The defect this pins, and the reason the resolver's own tests never caught it: they call
    /// <c>ResolveZoneKillCredit</c> directly, so they never see the hook that runs in production. That
    /// hook emptied the abuse list before calling the mirror, so <c>SelectZoneReportedTarget</c> found
    /// nothing, <c>HasEntry</c> was false so the "zone has an opinion" guard could not fire either,
    /// and the resolver fell through to the World-only paths this bridge exists to replace. The new
    /// selection had no effect in production and every death looked unattributed.
    /// </remarks>
    [Test]
    public async Task TheAbuseListIsStillReadableWhenTheKillIsMirrored()
    {
        NpcAbuserRegistry.Reset();
        const uint npc = Npc;
        const uint abuser = Abuser;
        NpcAbuserRegistry.Register(npc, abuser);

        // The mirror step is where the credit is resolved, so this is where the list must still be
        // whole. Capturing the registry state inside the callback is the only observation that sees
        // the ordering rather than the end result.
        var visibleAtResolve = false;
        var idsAtResolve = Array.Empty<uint>();

        AAEmu.Game.WorldIntegration.ResolveKillCreditThenForget(npc, _ =>
        {
            visibleAtResolve = NpcAbuserRegistry.HasEntry(npc);
            idsAtResolve = NpcAbuserRegistry.GetAbusers(npc).ToArray();
        });

        await Assert.That(visibleAtResolve).IsTrue();
        await Assert.That(idsAtResolve).IsEquivalentTo(new[] { abuser });
    }

    /// <summary>
    /// And the list is still gone afterwards - forgetting late must not have become forgetting never.
    /// </summary>
    [Test]
    public async Task TheAbuseListIsGoneOnceTheKillHasBeenMirrored()
    {
        NpcAbuserRegistry.Reset();
        const uint npc = Npc;
        NpcAbuserRegistry.Register(npc, Abuser);
        NpcAbuserRegistry.Register(npc, 0x0A0B0C);

        var mirrored = false;
        AAEmu.Game.WorldIntegration.ResolveKillCreditThenForget(npc, _ => mirrored = true);

        await Assert.That(mirrored).IsTrue();
        await Assert.That(NpcAbuserRegistry.HasEntry(npc)).IsFalse();
        await Assert.That(NpcAbuserRegistry.GetAbusers(npc)).IsEmpty();
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
    }

    /// <summary>
    /// Forgetting before resolving is the regression, pinned directly: it leaves nothing for the
    /// resolver to read and nothing for the guard to notice.
    /// </summary>
    [Test]
    public async Task ForgettingFirstWouldLeaveTheResolverNothingToRead()
    {
        NpcAbuserRegistry.Reset();
        const uint npc = Npc;
        NpcAbuserRegistry.Register(npc, Abuser);

        // What the hook used to do, run explicitly.
        NpcAbuserRegistry.ForgetNpc(npc);
        var visibleAtResolve = NpcAbuserRegistry.HasEntry(npc);
        var idsAtResolve = NpcAbuserRegistry.GetAbusers(npc).ToArray();

        await Assert.That(visibleAtResolve).IsFalse();
        await Assert.That(idsAtResolve).IsEmpty();
    }

    private static void Score(Npc npc, uint objId, int damage)
    {
        var aggro = new Aggro(new Unit { ObjId = objId });
        aggro.AddAggro(AggroKind.Damage, damage);
        npc.AggroTable[objId] = aggro;
    }

    [Test]
    public async Task Unregister_AlsoDropsTheWorldSideAggroRowOfTheListedIds()
    {
        var npc = MirrorNpc(Npc);
        Score(npc, Abuser, 100);
        Score(npc, Abuser + 1, 80);
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser + 1));

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, Abuser));

        await Assert.That(handled).IsTrue();
        // The zone owns the list, so a row it just dropped must not go on winning target queries.
        await Assert.That(npc.AggroTable.ContainsKey(Abuser)).IsFalse();
        await Assert.That(npc.AggroTable.ContainsKey(Abuser + 1)).IsTrue();
    }

    [Test]
    public async Task Clear_AlsoDropsEveryWorldSideAggroRowOfThatNpcOnly()
    {
        const uint otherNpc = 0x040506;
        const uint neverReported = 0x07080A;
        var npc = MirrorNpc(Npc);
        var other = MirrorNpc(otherNpc);
        Score(npc, Abuser, 100);
        Score(npc, neverReported, 70);
        Score(other, Abuser, 100);
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(otherNpc, Abuser));

        var handled = Handle(ZwOpcodes.ClearNpcAbusers, ClearBody(Npc));

        await Assert.That(handled).IsTrue();
        // A clear says the npc has no abusers at all, so a World row the zone never even named is
        // part of what goes.
        await Assert.That(npc.AggroTable.IsEmpty).IsTrue();
        // The clear names one npc: another npc's row is not collateral.
        await Assert.That(other.AggroTable.ContainsKey(Abuser)).IsTrue();
    }

    [Test]
    public async Task ARemovalForAnNpcTheWorldDoesNotMirrorIsAccepted()
    {
        // No mirror exists for Npc in this test's world: the zone can report an abuse the World
        // never scored, and that is not a parse failure.
        Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, Abuser));

        await Assert.That(Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, Abuser))).IsTrue();
        await Assert.That(Handle(ZwOpcodes.ClearNpcAbusers, ClearBody(Npc))).IsTrue();
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
    }

    [Test]
    public async Task EveryRemovalPathTogether_LeaveNothingBehind()
    {
        // The lifecycle the row depends on: register, unregister, npc gone, abuser logout, clear.
        for (var i = 1u; i <= 4u; i++)
            Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc * 100 + i, Abuser + i));
        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(4);

        Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc * 100 + 1, Abuser + 1));
        NpcAbuserRegistry.ForgetNpc(Npc * 100 + 2);
        NpcAbuserRegistry.ForgetUnit(Abuser + 3);
        Handle(ZwOpcodes.ClearNpcAbusers, ClearBody(Npc * 100 + 4));

        await Assert.That(NpcAbuserRegistry.TotalEntryCount).IsEqualTo(0);
        await Assert.That(NpcAbuserRegistry.TrackedNpcCount).IsEqualTo(0);
    }
}
