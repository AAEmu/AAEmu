using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.UnitTests.Game.Models.Game.Team.Recruitment;

public class RaidRecruitWireTests
{
    private static RaidRecruitRecord Record() => new(
        OwnerId: 0x1122334455667788, OwnerName: "Recruiter", OwnerLevel: 55, OwnerAbility1: 1, OwnerAbility2: 4,
        OwnerAbility3: 9, OwnerExpeditionId: 1234, TypeId: 1, SubTypeId: 3, Headcount: 5, LimitLevel: 30,
        LimitGearPoint: 2000, AutoJoin: true, Message: "tank wanted", Hour: 21, Minute: 30, ApplicantCount: 2,
        MemberCount: 3, LeadershipPoint: 400, GearPoint: 11000, CreateTime: 1_800_000_000, ExpireTime: 1_800_003_600,
        AddExpireTime: 0);

    [Test]
    public async Task Record_RoundTrips()
    {
        var stream = new PacketStream();
        RaidRecruitWire.WriteRecord(stream, Record());

        var read = RaidRecruitWire.ReadRecord(new PacketStream(stream));

        await Assert.That(read).IsEqualTo(Record());
    }

    [Test]
    public async Task Record_FixedPartIsEightyNineBytesPlusTwoLengthPrefixedStrings()
    {
        // u64 + i8 x4 + i32 x2 + i32 x2 + u32 x3 + bool + u32 x2 + i32 x4 + i64 x3 = 89 bytes around the
        // two strings, each carried as an i16 length and its UTF-8 bytes.
        var stream = new PacketStream();
        RaidRecruitWire.WriteRecord(stream, Record() with { OwnerName = "", Message = "" });
        await Assert.That(stream.Count).IsEqualTo(93);

        stream = new PacketStream();
        RaidRecruitWire.WriteRecord(stream, Record());
        await Assert.That(stream.Count).IsEqualTo(93 + "Recruiter".Length + "tank wanted".Length);
    }

    [Test]
    public async Task Record_StartsWithTheOwnerIdAndCarriesAZeroAtTheUnreadSlot()
    {
        var stream = new PacketStream();
        RaidRecruitWire.WriteRecord(stream, Record() with { OwnerName = "" });
        var bytes = stream.GetBytes();

        await Assert.That(BitConverter.ToUInt64(bytes, 0)).IsEqualTo(0x1122334455667788UL);
        // After u64, empty string (2), four i8 and the expedition id comes the unnamed i32 at +0x9c.
        await Assert.That(BitConverter.ToInt32(bytes, 8 + 2 + 4)).IsEqualTo(1234);
        await Assert.That(BitConverter.ToInt32(bytes, 8 + 2 + 4 + 4)).IsEqualTo(0);
        await Assert.That(BitConverter.ToInt32(bytes, 8 + 2 + 4 + 8)).IsEqualTo(1);
        await Assert.That(BitConverter.ToInt32(bytes, 8 + 2 + 4 + 12)).IsEqualTo(3);
    }

    [Test]
    public async Task Applicant_RoundTrips()
    {
        var applicant = new RaidApplicantRecord(42, "Applicant", 57, 2, 5, 8, Role: 4, GearPoint: 7000);
        var stream = new PacketStream();
        RaidRecruitWire.WriteApplicant(stream, applicant);

        var read = RaidRecruitWire.ReadApplicant(new PacketStream(stream));

        await Assert.That(read).IsEqualTo(applicant);
        // u64 + string(2 + 9) + i8 x4 + u32 + i32
        await Assert.That(stream.Count).IsEqualTo(8 + 11 + 4 + 4 + 4);
    }

    [Test]
    public async Task Detail_LaysOutTheJoinDialogFields()
    {
        var stream = new PacketStream();
        RaidRecruitWire.WriteDetail(stream, Record() with { OwnerName = "", Message = "" });
        var bytes = stream.GetBytes();

        // u64, string(2), i8, i32 expedition, i32 type, i32 subType, u32 limitLevel, u32 limitGearPoint,
        // string(2), u32 hour, u32 minute, i64 createTime
        await Assert.That(bytes.Length).IsEqualTo(8 + 2 + 1 + 12 + 8 + 2 + 8 + 8);
        await Assert.That(BitConverter.ToInt32(bytes, 11)).IsEqualTo(1234);
        await Assert.That(BitConverter.ToInt32(bytes, 15)).IsEqualTo(1);
        await Assert.That(BitConverter.ToInt32(bytes, 19)).IsEqualTo(3);
        await Assert.That(BitConverter.ToUInt32(bytes, 23)).IsEqualTo(30u);
        await Assert.That(BitConverter.ToUInt32(bytes, 27)).IsEqualTo(2000u);
        await Assert.That(BitConverter.ToUInt32(bytes, 33)).IsEqualTo(21u);
        await Assert.That(BitConverter.ToUInt32(bytes, 37)).IsEqualTo(30u);
        await Assert.That(BitConverter.ToInt64(bytes, 41)).IsEqualTo(1_800_000_000L);
    }
}
