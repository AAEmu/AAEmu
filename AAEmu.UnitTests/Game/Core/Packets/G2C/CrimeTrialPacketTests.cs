using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// Wire layout of the crime / jury / trial family. Field order, widths and names come from the
/// 10.0.2.13 client's own readers, which pass each field's name inline; these tests pin the byte
/// count and the position of every field whose width was corrected or added, so a future edit cannot
/// silently shift the stream the client parses.
/// </summary>
public class CrimeTrialPacketTests
{
    private static byte[] Body(Action<PacketStream> write)
    {
        var stream = new PacketStream();
        write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task AskImprisonOrTrial_CarriesTheOfferAndTheJailMinutes()
    {
        var body = Body(s => new SCAskImprisonOrTrialPacket(5, 120).Write(s));

        await Assert.That(body.Length).IsEqualTo(4 + 4);
        await Assert.That(BitConverter.ToUInt32(body, 0)).IsEqualTo(5u);
        await Assert.That(BitConverter.ToUInt32(body, 4)).IsEqualTo(120u);
    }

    [Test]
    public async Task CrimeChanged_PointsRecordAndLockupFlag()
    {
        // Verified body: i32 point, i16 crimePoint, i32 crimeRecord, i16 crimeScore, bool lockup.
        var body = Body(s => new SCCrimeChangedPacket(60, 3, 2, 1, true).Write(s));

        await Assert.That(body.Length).IsEqualTo(4 + 2 + 4 + 2 + 1);
        await Assert.That(BitConverter.ToInt32(body, 0)).IsEqualTo(60);
        await Assert.That(body[12]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task CrimeData_DefendantTrialAndEvidenceDoodad()
    {
        var body = Body(s => new SCCrimeDataPacket(1, "Feos", 18, 2, 77ul, 30, 0x123456).Write(s));

        // u64 type + string + u8 race + u32 type + u64 trialId + u32 sentence + bc(3)
        await Assert.That(body.Length).IsEqualTo(8 + (2 + 4) + 1 + 4 + 8 + 4 + 3);
        await Assert.That(BitConverter.ToUInt64(body, 8 + (2 + 4) + 1 + 4)).IsEqualTo(77ul);
    }

    [Test]
    public async Task CrimeRecords_TrialIdIsSixtyFourBit()
    {
        var body = Body(s => new SCCrimeRecordsPacket(0x1_0000_0002ul, 1, 5, 3).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 4 + 4 + 4);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0002ul);
    }

    [Test]
    public async Task ChangeTrialState_TrialIdIsSixtyFourBit()
    {
        var body = Body(s => new SCChangeTrialStatePacket(0x1_0000_0003ul, 4, 3, 90).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 1 + 4 + 4);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0003ul);
        await Assert.That(body[8]).IsEqualTo((byte)4);
    }

    [Test]
    public async Task TrialInfo_WholeTally()
    {
        var body = Body(s => new SCTrialInfoPacket(0x1_0000_0004ul, 60, 1, 2, 5, 6, 0, 6, 6, 3, 0).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 10 * 4);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0004ul);
    }

    [Test]
    public async Task SummonJury_TrialIdIsSixtyFourBit()
    {
        var body = Body(s => new SCSummonJuryPacket(0x1_0000_0005ul, 12, 1).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 4 + 4);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0005ul);
    }

    [Test]
    public async Task JuryBeSeated_WestFlagThenTrialId()
    {
        var body = Body(s => new SCJuryBeSeatedPacket(true, 0x1_0000_0006ul, 12, 2).Write(s));

        await Assert.That(body.Length).IsEqualTo(1 + 8 + 4 + 4);
        await Assert.That(body[0]).IsEqualTo((byte)1);
        await Assert.That(BitConverter.ToUInt64(body, 1)).IsEqualTo(0x1_0000_0006ul);
    }

    [Test]
    public async Task InviteJury_DefendantNameThenTrialId()
    {
        var body = Body(s => new SCInviteJuryPacket("Feos", 0x1_0000_0007ul).Write(s));

        await Assert.That(body.Length).IsEqualTo((2 + 4) + 8);
        await Assert.That(BitConverter.ToUInt64(body, 2 + 4)).IsEqualTo(0x1_0000_0007ul);
    }

    [Test]
    public async Task TrialCancelled_TrialIdIsSixtyFourBit()
    {
        var body = Body(s => new SCTrialCancledPacket(0x1_0000_0008ul).Write(s));

        await Assert.That(body.Length).IsEqualTo(8);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0008ul);
    }

    [Test]
    public async Task BotSuspectArrested_TwoTypeFieldsThenBothNames()
    {
        // Both leading fields are separate u64s named "type" in the client's own reader.
        var body = Body(s => new SCBotSuspectArrestedPacket(1ul, 2ul, "Sheriff", "Feos").Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 8 + (2 + 7) + (2 + 4));
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(1ul);
        await Assert.That(BitConverter.ToUInt64(body, 8)).IsEqualTo(2ul);
    }

    [Test]
    public async Task SuspectGoingBotTrial_TwoTypesThenKicked()
    {
        var body = Body(s => new SCSuspectGoingBotTrialPacket(1ul, 2ul, true).Write(s));

        await Assert.That(body.Length).IsEqualTo(8 + 8 + 1);
        await Assert.That(body[16]).IsEqualTo((byte)1);
    }

    [Test]
    public async Task AudiencePackets_CarryTheMemberObjectIdAndName()
    {
        var joined = Body(s => new SCTrialAudienceJoinedPacket(9ul, 0x123456, "Feos").Write(s));
        // u64 trialId + bc(3) + string
        await Assert.That(joined.Length).IsEqualTo(8 + 3 + (2 + 4));

        var left = Body(s => new SCTrialAudienceLeftPacket(0x123456, "Feos").Write(s));
        // bc(3) + string - the client's reader for this one has no trial id
        await Assert.That(left.Length).IsEqualTo(3 + (2 + 4));
    }

    [Test]
    public async Task SummonDefendant_TrialIdIsSixtyFourBit()
    {
        var body = Body(s => new SCSummonDefendantPacket(0x1_0000_0009ul).Write(s));

        await Assert.That(body.Length).IsEqualTo(8);
        await Assert.That(BitConverter.ToUInt64(body, 0)).IsEqualTo(0x1_0000_0009ul);
    }

    [Test]
    public async Task RulingStatus_CountsThenTheVerdictAndTheSentenceClock()
    {
        // Verified body: i32 count, i32 total, u8 verdict, u32 sentence (milliseconds).
        var body = Body(s => new SCRulingStatusPacket(4, 5, TrialVerdictRules.NotGuiltyChoice, 1_800_000u).Write(s));

        await Assert.That(body.Length).IsEqualTo(4 + 4 + 1 + 4);
        await Assert.That(BitConverter.ToInt32(body, 0)).IsEqualTo(4);
        await Assert.That(BitConverter.ToInt32(body, 4)).IsEqualTo(5);
        await Assert.That(body[8]).IsEqualTo(TrialVerdictRules.NotGuiltyChoice);
        await Assert.That(BitConverter.ToUInt32(body, 9)).IsEqualTo(1_800_000u);
    }

    [Test]
    public async Task RulingClosed_HasAnEmptyBody()
    {
        // This empty close is what hides every trial window, so it must not carry a single byte.
        await Assert.That(Body(s => new SCRulingClosedPacket().Write(s)).Length).IsEqualTo(0);
    }

    [Test]
    public async Task WaitStatus_CarriesTheClocksInMilliseconds()
    {
        // Both wait dialogs are the defendant's, and the sentence line they print is a millisecond
        // clock - a seconds value shows up as a thousandth of its face value.
        var jury = Body(s => new SCJuryWaitStatusPacket(2, 5, 1_800_000u).Write(s));
        await Assert.That(jury.Length).IsEqualTo(4 + 4 + 4);
        await Assert.That(BitConverter.ToUInt32(jury, 8)).IsEqualTo(1_800_000u);

        var trial = Body(s => new SCTrialWaitStatusPacket(3, 1_800_000u).Write(s));
        await Assert.That(trial.Length).IsEqualTo(4 + 4);
        await Assert.That(BitConverter.ToUInt32(trial, 4)).IsEqualTo(1_800_000u);
    }

    [Test]
    public async Task TrialFamily_OpcodesMatchTheClientsOwnTable()
    {
        // The client's packet table numbers this whole family contiguously. The demo-mode, security
        // and debug packets that used to share these opcodes are gone from SCOffsets, so pin the block
        // that actually belongs here.
        await Assert.That(SCOffsets.SCCrimeChangedPacket).IsEqualTo((ushort)0x1B6);
        await Assert.That(SCOffsets.SCCriminalArrestedPacket).IsEqualTo((ushort)0x1B7);
        await Assert.That(SCOffsets.SCAskImprisonOrTrialPacket).IsEqualTo((ushort)0x1B8);
        await Assert.That(SCOffsets.SCInviteJuryPacket).IsEqualTo((ushort)0x1B9);
        await Assert.That(SCOffsets.SCSummonJuryPacket).IsEqualTo((ushort)0x1BA);
        await Assert.That(SCOffsets.SCJuryBeSeatedPacket).IsEqualTo((ushort)0x1BB);
        await Assert.That(SCOffsets.SCSummonDefendantPacket).IsEqualTo((ushort)0x1BC);
        await Assert.That(SCOffsets.SCCrimeDataPacket).IsEqualTo((ushort)0x1BD);
        await Assert.That(SCOffsets.SCCrimeRecordsPacket).IsEqualTo((ushort)0x1BE);
        await Assert.That(SCOffsets.SCChangeTrialStatePacket).IsEqualTo((ushort)0x1BF);
        await Assert.That(SCOffsets.SCChangeJuryOKCountPacket).IsEqualTo((ushort)0x1C0);
        await Assert.That(SCOffsets.SCChangeJuryVerdictCountPacket).IsEqualTo((ushort)0x1C1);
        await Assert.That(SCOffsets.SCTrialWaitStatusPacket).IsEqualTo((ushort)0x1C2);
        await Assert.That(SCOffsets.SCJuryWaitStatusPacket).IsEqualTo((ushort)0x1C3);
        await Assert.That(SCOffsets.SCRulingStatusPacket).IsEqualTo((ushort)0x1C4);
        await Assert.That(SCOffsets.SCRulingClosedPacket).IsEqualTo((ushort)0x1C5);
        await Assert.That(SCOffsets.SCTrialAudienceJoinedPacket).IsEqualTo((ushort)0x1C6);
        await Assert.That(SCOffsets.SCTrialAudienceLeftPacket).IsEqualTo((ushort)0x1C7);
        await Assert.That(SCOffsets.SCTrialInfoPacket).IsEqualTo((ushort)0x1C8);
        await Assert.That(SCOffsets.SCJuryWaitingNumberPacket).IsEqualTo((ushort)0x1C9);
        await Assert.That(SCOffsets.SCTrialCancledPacket).IsEqualTo((ushort)0x1CA);
        await Assert.That(SCOffsets.SCJuryPointChangedPacket).IsEqualTo((ushort)0x1D1);
    }
}
