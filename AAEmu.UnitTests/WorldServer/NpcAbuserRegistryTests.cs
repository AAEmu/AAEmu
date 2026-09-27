using AAEmu.Commons.Network;
using AAEmu.World.Core.Packets.Zw;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.WorldServer;

/// <summary>
/// The zone reports its npc abuser lists with a register, a batched unregister and a clear.
/// These cover the wire shape of all three and the removal paths the events do not cover: an npc
/// leaving the world and an abuser leaving the world. Without those, a row survives both of its
/// participants and answers later queries with a unit nothing owns.
/// </summary>
[NotInParallel]
public class NpcAbuserRegistryTests
{
    private const uint Npc = 0x010203;
    private const uint Abuser = 0x0A0B0C;

    [Before(Test)]
    public void ResetRegistry() => NpcAbuserRegistry.Reset();

    [After(Test)]
    public void ClearRegistry() => NpcAbuserRegistry.Reset();

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
    public async Task Unregister_AcceptsAFullHundredIdBatch()
    {
        // The zone splits longer lists across events, so 100 in one event is the legitimate maximum.
        var ids = Enumerable.Range(1, NpcAbuserRegistry.MaxUnregisterBatch)
            .Select(i => (uint)(0x1000 + i))
            .ToArray();
        foreach (var id in ids)
            Handle(ZwOpcodes.RegisterNpcAbuser, RegisterBody(Npc, id));

        var handled = Handle(ZwOpcodes.UnregisterNpcAbusers, UnregisterBody(Npc, ids));

        await Assert.That(handled).IsTrue();
        await Assert.That(NpcAbuserRegistry.GetAbusers(Npc)).IsEmpty();
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
    public async Task Unregister_RejectsACountBeyondOneBatch()
    {
        var body = new PacketStream();
        body.WriteBc(Npc);
        body.Write((byte)(NpcAbuserRegistry.MaxUnregisterBatch + 1));
        for (var i = 0; i < NpcAbuserRegistry.MaxUnregisterBatch + 1; i++)
            body.WriteBc((uint)(0x2000 + i));
        var raw = body.GetBytes();

        await Assert.That(Handle(ZwOpcodes.UnregisterNpcAbusers, raw)).IsFalse();
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
