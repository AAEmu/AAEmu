using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Team.Recruitment;

namespace AAEmu.UnitTests.Game.Models.Game.Team.Recruitment;

public class RaidRecruitRulesTests
{
    // The shipped rows: raid_recruit_types 1 (dungeon) and 4 (etc), raid_recruit_sub_types 3 (Burnt Castle
    // Armory, type 1, level 30, gear 2000) and 19 (Recruit Any: Misc, type 4, level 18, gear 0),
    // raid_recruit_headcounts 3/5/10/25/50, the four raid_recruit_time_and_expenses bands at 15000 and
    // content_configs 385 = 20000. Type 9 is invented and hidden.
    private static RaidRecruitContent Content(long maxGear = 20000) => new(
        new Dictionary<int, RaidRecruitType>
        {
            [1] = new(1, "dungeon", true, "dungeon"),
            [4] = new(4, "etc", true, "etc"),
            [9] = new(9, "hidden", false, "hidden")
        },
        new Dictionary<int, RaidRecruitSubType>
        {
            [3] = new(3, "Burnt Castle Armory", 1, 30, "", 2000),
            [19] = new(19, "Recruit Any: Misc.", 4, 18, "", 0)
        },
        [3, 5, 10, 25, 50],
        [new(1, 1, 6, 15000), new(2, 7, 10, 15000), new(3, 11, 18, 15000), new(4, 19, 24, 15000)],
        maxGear);

    private static RaidRecruitPostRequest Request(
        int type = 1, int subType = 3, uint headcount = 5, uint level = 30, uint gear = 2000,
        string message = "tank wanted", uint hour = 21, uint minute = 30, bool autoJoin = false) =>
        new(type, subType, headcount, level, autoJoin, message, hour, minute, gear);

    private static RaidRecruitError Validate(RaidRecruitPostRequest request, int members = 1, RaidRecruitContent content = null) =>
        RaidRecruitRules.ValidatePost(request, content ?? Content(), members, levelCap: 55, maxHeirLevel: 5);

    [Test]
    public async Task Constants_PinTheClientValues()
    {
        await Assert.That(RaidRecruitRules.MaxApplicationsPerCharacter).IsEqualTo(3);
        await Assert.That(RaidRecruitRules.MaxApplicantsPerRecruitment).IsEqualTo(100);
        await Assert.That(RaidRecruitRules.ListLimit).IsEqualTo(50);
        await Assert.That(RaidRecruitRules.MessageByteLimit).IsEqualTo(200);
        await Assert.That(RaidRecruitRules.ExpireDelayMinutes).IsEqualTo(20);
        await Assert.That(RaidRecruitRules.DefaultExpense).IsEqualTo(10000L);
        // The largest headcount on offer is exactly the raid's slot count.
        await Assert.That(Content().Headcounts[^1]).IsEqualTo(AAEmu.Game.Models.Game.Team.Team.RaidMemberLimit);
        await Assert.That((int)MemberRole.RangedAttacker).IsEqualTo(4);
    }

    [Test]
    public async Task ValidatePost_ShippedRowsPass()
    {
        await Assert.That(Validate(Request())).IsEqualTo(RaidRecruitError.None);
        await Assert.That(Validate(Request(type: 4, subType: 19, level: 18, gear: 0, headcount: 3))).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task ValidatePost_TypeMustExistAndBeVisible()
    {
        await Assert.That(Validate(Request(type: 7))).IsEqualTo(RaidRecruitError.UnknownType);
        await Assert.That(Validate(Request(type: 9))).IsEqualTo(RaidRecruitError.UnknownType);
    }

    [Test]
    public async Task ValidatePost_SubTypeMustExistAndBelongToTheType()
    {
        await Assert.That(Validate(Request(subType: 99))).IsEqualTo(RaidRecruitError.UnknownSubType);
        await Assert.That(Validate(Request(type: 4, subType: 3))).IsEqualTo(RaidRecruitError.SubTypeNotOfType);
    }

    [Test]
    public async Task ValidatePost_HeadcountComesFromTheTableAndExceedsMembers()
    {
        await Assert.That(Validate(Request(headcount: 4))).IsEqualTo(RaidRecruitError.InvalidHeadcount);
        await Assert.That(Validate(Request(headcount: 0))).IsEqualTo(RaidRecruitError.InvalidHeadcount);
        await Assert.That(Validate(Request(headcount: 5), members: 5)).IsEqualTo(RaidRecruitError.HeadcountNotAboveMembers);
        await Assert.That(Validate(Request(headcount: 5), members: 4)).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task ValidatePost_LevelRunsFromSubTypeFloorToCapPlusHeir()
    {
        await Assert.That(Validate(Request(level: 29))).IsEqualTo(RaidRecruitError.LevelBelowSubType);
        await Assert.That(Validate(Request(level: 60))).IsEqualTo(RaidRecruitError.None);
        await Assert.That(Validate(Request(level: 61))).IsEqualTo(RaidRecruitError.LevelAboveCap);
    }

    [Test]
    public async Task ValidatePost_GearRunsFromSubTypeFloorToContentConfig385()
    {
        await Assert.That(Validate(Request(gear: 1999))).IsEqualTo(RaidRecruitError.GearBelowSubType);
        await Assert.That(Validate(Request(gear: 20000))).IsEqualTo(RaidRecruitError.None);
        await Assert.That(Validate(Request(gear: 20001))).IsEqualTo(RaidRecruitError.GearAboveCap);
    }

    [Test]
    public async Task ValidatePost_MissingMaxGearScoreFailsClosed()
    {
        await Assert.That(Validate(Request(gear: 2000), content: Content(maxGear: 0))).IsEqualTo(RaidRecruitError.GearAboveCap);
    }

    [Test]
    public async Task ValidatePost_MessageIsLimitedByUtf8Bytes()
    {
        await Assert.That(Validate(Request(message: new string('a', 200)))).IsEqualTo(RaidRecruitError.None);
        await Assert.That(Validate(Request(message: new string('a', 201)))).IsEqualTo(RaidRecruitError.MessageTooLong);
        // 67 Hangul syllables are 201 bytes.
        await Assert.That(Validate(Request(message: new string('가', 67)))).IsEqualTo(RaidRecruitError.MessageTooLong);
        await Assert.That(Validate(Request(message: null))).IsEqualTo(RaidRecruitError.MessageTooLong);
        await Assert.That(Validate(Request(message: ""))).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task ValidatePost_DepartureIsAClockTime()
    {
        await Assert.That(Validate(Request(hour: 23, minute: 59))).IsEqualTo(RaidRecruitError.None);
        await Assert.That(Validate(Request(hour: 24))).IsEqualTo(RaidRecruitError.InvalidTime);
        await Assert.That(Validate(Request(minute: 60))).IsEqualTo(RaidRecruitError.InvalidTime);
    }

    [Test]
    public async Task Departure_LaterTodayStaysToday()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 30, 15, TimeSpan.FromHours(2));
        var departure = RaidRecruitRules.Departure(now, 18, 0);
        await Assert.That(departure).IsEqualTo(new DateTimeOffset(2026, 9, 21, 18, 0, 15, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task Departure_EarlierOrEqualRollsToTomorrow()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 30, 0, TimeSpan.Zero);
        await Assert.That(RaidRecruitRules.Departure(now, 9, 0).Day).IsEqualTo(22);
        // Same minute counts as passed (the client tests minute <= now.minute).
        await Assert.That(RaidRecruitRules.Departure(now, 12, 30).Day).IsEqualTo(22);
        await Assert.That(RaidRecruitRules.Departure(now, 12, 40).Day).IsEqualTo(21);
    }

    [Test]
    public async Task HoursUntil_TruncatesLikeTheClient()
    {
        var now = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        await Assert.That(RaidRecruitRules.HoursUntil(now.AddHours(5).AddMinutes(59), now)).IsEqualTo(5);
        await Assert.That(RaidRecruitRules.HoursUntil(now.AddHours(6), now)).IsEqualTo(6);
        await Assert.That(RaidRecruitRules.HoursUntil(now.AddMinutes(10), now)).IsEqualTo(0);
        await Assert.That(RaidRecruitRules.HoursUntil(now.AddMinutes(-10), now)).IsEqualTo(0);
    }

    [Test]
    public async Task ExpenseFor_BandsCoverOneToTwentyFourHoursAndDefaultOutside()
    {
        var bands = Content().ExpenseBands;
        await Assert.That(RaidRecruitRules.ExpenseFor(0, bands)).IsEqualTo(10000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(1, bands)).IsEqualTo(15000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(6, bands)).IsEqualTo(15000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(7, bands)).IsEqualTo(15000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(24, bands)).IsEqualTo(15000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(25, bands)).IsEqualTo(10000L);
        await Assert.That(RaidRecruitRules.ExpenseFor(3, [])).IsEqualTo(10000L);
    }

    [Test]
    public async Task Expiry_IsTwentyMinutesAfterDeparture()
    {
        var departure = new DateTimeOffset(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
        var expire = RaidRecruitRules.ExpireUnixTime(departure);
        await Assert.That(expire).IsEqualTo(departure.ToUnixTimeSeconds() + 1200);
        await Assert.That(RaidRecruitRules.IsExpired(expire, expire - 1)).IsFalse();
        await Assert.That(RaidRecruitRules.IsExpired(expire, expire)).IsTrue();
    }

    [Test]
    public async Task CanPost_SoloOwnerAndRaidOfficerOnly()
    {
        await Assert.That(RaidRecruitRules.CanPost(7, TeamFacts.None)).IsTrue();
        var party = new TeamFacts(1, OwnerId: 7, OfficerId: 0, IsParty: true, ActorIsMember: true);
        await Assert.That(RaidRecruitRules.CanPost(7, party)).IsTrue();
        await Assert.That(RaidRecruitRules.CanPost(8, party with { ActorIsMember = true })).IsFalse();
        var raid = new TeamFacts(2, OwnerId: 7, OfficerId: 8, IsParty: false, ActorIsMember: true);
        await Assert.That(RaidRecruitRules.CanPost(8, raid)).IsTrue();
        await Assert.That(RaidRecruitRules.CanPost(9, raid)).IsFalse();
        // A party has no officers, so the officer id means nothing there.
        await Assert.That(RaidRecruitRules.CanPost(8, party with { OfficerId = 8 })).IsFalse();
    }

    [Test]
    public async Task CanManage_PosterOrInviterOfTheRecruitingTeam()
    {
        var raid = new TeamFacts(2, OwnerId: 7, OfficerId: 8, IsParty: false, ActorIsMember: true);
        await Assert.That(RaidRecruitRules.CanManage(7, recruitOwnerId: 7, recruitTeamId: 2, raid)).IsTrue();
        await Assert.That(RaidRecruitRules.CanManage(8, recruitOwnerId: 7, recruitTeamId: 2, raid)).IsTrue();
        await Assert.That(RaidRecruitRules.CanManage(9, recruitOwnerId: 7, recruitTeamId: 2, raid)).IsFalse();
        // Another team's officer cannot manage it.
        await Assert.That(RaidRecruitRules.CanManage(8, recruitOwnerId: 7, recruitTeamId: 2, raid with { TeamId = 3 })).IsFalse();
        // A solo post answers to its poster alone.
        await Assert.That(RaidRecruitRules.CanManage(8, recruitOwnerId: 7, recruitTeamId: 0, raid)).IsFalse();
        await Assert.That(RaidRecruitRules.CanManage(7, recruitOwnerId: 7, recruitTeamId: 0, TeamFacts.None)).IsTrue();
    }

    [Test]
    public async Task CanViewApplicants_AnyMemberOfTheRecruitingTeam()
    {
        var raid = new TeamFacts(2, OwnerId: 7, OfficerId: 8, IsParty: false, ActorIsMember: true);
        await Assert.That(RaidRecruitRules.CanViewApplicants(9, recruitOwnerId: 7, recruitTeamId: 2, raid)).IsTrue();
        await Assert.That(RaidRecruitRules.CanViewApplicants(9, recruitOwnerId: 7, recruitTeamId: 2, raid with { TeamId = 3 })).IsFalse();
        await Assert.That(RaidRecruitRules.CanViewApplicants(9, recruitOwnerId: 7, recruitTeamId: 0, TeamFacts.None)).IsFalse();
    }

    [Test]
    public async Task CanEnableAutoJoin_NeedsAnEmptyApplicantList()
    {
        await Assert.That(RaidRecruitRules.CanEnableAutoJoin(0)).IsTrue();
        await Assert.That(RaidRecruitRules.CanEnableAutoJoin(1)).IsFalse();
    }

    private static RaidApplyCheck Check() => new(
        ApplicantId: 20, OwnerId: 7, ApplicantTeamId: 0, RecruitTeamId: 2, ApplicantIsRecruiting: false,
        AlreadyApplied: false, PendingApplications: 0, MemberCount: 3, Headcount: 10, ApplicantCount: 0,
        ApplicantLevel: 55, ApplicantHeirLevel: 0, LimitLevel: 55, ApplicantGearScore: 5000, LimitGearPoint: 5000,
        Relation: RelationState.Friendly, Stamp: 1000, CreateTime: 1000, ExpireTime: 2000, NowUnix: 1500);

    [Test]
    public async Task CanApply_EligibleApplicantPasses()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check())).IsEqualTo(RaidRecruitError.None);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { Relation = RelationState.Neutral })).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task CanApply_ExpiredOrStaleRowsAreRefusedFirst()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { NowUnix = 2000 })).IsEqualTo(RaidRecruitError.Expired);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { Stamp = 999 })).IsEqualTo(RaidRecruitError.StaleStamp);
    }

    [Test]
    public async Task CanApply_OwnPostAndOwnTeamAreTheSameRefusal()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantId = 7 })).IsEqualTo(RaidRecruitError.OwnRaid);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantTeamId = 2 })).IsEqualTo(RaidRecruitError.OwnRaid);
        // A solo post has no team, so another team's member is not "in it".
        await Assert.That(RaidRecruitRules.CanApply(Check() with { RecruitTeamId = 0, ApplicantTeamId = 5 })).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task CanApply_RecruitersAndDuplicatesAreRefused()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantIsRecruiting = true })).IsEqualTo(RaidRecruitError.ApplicantIsRecruiting);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { AlreadyApplied = true })).IsEqualTo(RaidRecruitError.Duplicate);
    }

    [Test]
    public async Task CanApply_ThreeApplicationsIsTheCap()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { PendingApplications = 2 })).IsEqualTo(RaidRecruitError.None);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { PendingApplications = 3 })).IsEqualTo(RaidRecruitError.TooManyApplications);
    }

    [Test]
    public async Task CanApply_FullPostsAreRefused()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { MemberCount = 10 })).IsEqualTo(RaidRecruitError.RecruitFull);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantCount = 100 })).IsEqualTo(RaidRecruitError.ApplicantListFull);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantCount = 99 })).IsEqualTo(RaidRecruitError.None);
    }

    [Test]
    public async Task CanApply_LevelCountsHeirLevels()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantLevel = 54 })).IsEqualTo(RaidRecruitError.LevelTooLow);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantLevel = 55, LimitLevel = 58, ApplicantHeirLevel = 3 })).IsEqualTo(RaidRecruitError.None);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantLevel = 55, LimitLevel = 58, ApplicantHeirLevel = 2 })).IsEqualTo(RaidRecruitError.LevelTooLow);
    }

    [Test]
    public async Task CanApply_GearAndHostilityAreRefused()
    {
        await Assert.That(RaidRecruitRules.CanApply(Check() with { ApplicantGearScore = 4999 })).IsEqualTo(RaidRecruitError.GearTooLow);
        await Assert.That(RaidRecruitRules.CanApply(Check() with { Relation = RelationState.Hostile })).IsEqualTo(RaidRecruitError.Hostile);
    }

    [Test]
    public async Task OpenSeats_HeadcountAndRaidLimitBothBind()
    {
        await Assert.That(RaidRecruitRules.OpenSeats(memberCount: 4, acceptedPending: 2, headcount: 10, memberLimit: 50)).IsEqualTo(4);
        await Assert.That(RaidRecruitRules.OpenSeats(memberCount: 50, acceptedPending: 0, headcount: 50, memberLimit: 50)).IsEqualTo(0);
        await Assert.That(RaidRecruitRules.OpenSeats(memberCount: 1, acceptedPending: 0, headcount: 200, memberLimit: 50)).IsEqualTo(49);
        await Assert.That(RaidRecruitRules.OpenSeats(memberCount: 6, acceptedPending: 0, headcount: 5, memberLimit: 50)).IsEqualTo(0);
    }

    [Test]
    public async Task ToMemberRole_MapsTheFiveClientRolesAndNothingElse()
    {
        await Assert.That(RaidRecruitRules.ToMemberRole(0)).IsEqualTo(MemberRole.Undecided);
        await Assert.That(RaidRecruitRules.ToMemberRole(1)).IsEqualTo(MemberRole.Tank);
        await Assert.That(RaidRecruitRules.ToMemberRole(2)).IsEqualTo(MemberRole.Healer);
        await Assert.That(RaidRecruitRules.ToMemberRole(3)).IsEqualTo(MemberRole.Attacker);
        await Assert.That(RaidRecruitRules.ToMemberRole(4)).IsEqualTo(MemberRole.RangedAttacker);
        await Assert.That(RaidRecruitRules.ToMemberRole(5)).IsEqualTo(MemberRole.Undecided);
        await Assert.That(RaidRecruitRules.ToMemberRole(uint.MaxValue)).IsEqualTo(MemberRole.Undecided);
    }

    [Test]
    public async Task ToErrorMessage_UsesTheClientCodesAndStaysSilentOtherwise()
    {
        await Assert.That(RaidRecruitRules.ToErrorMessage(RaidRecruitError.OwnRaid)).IsEqualTo(ErrorMessageType.RaidCannotApplyMyRaid);
        await Assert.That((int)ErrorMessageType.RaidCannotApplyMyRaid).IsEqualTo(1012);
        await Assert.That((int)ErrorMessageType.RaidApplyIsDuplicate).IsEqualTo(1013);
        await Assert.That((int)ErrorMessageType.RaidCannotRequiredLevel).IsEqualTo(1016);
        await Assert.That((int)ErrorMessageType.NotJoinRaid).IsEqualTo(1002);
        await Assert.That((int)ErrorMessageType.NotChangeRaidRecruitOwner).IsEqualTo(1000);
        await Assert.That((int)ErrorMessageType.NotChangeRaidRecruitTarget).IsEqualTo(1001);
        await Assert.That(RaidRecruitRules.ToErrorMessage(RaidRecruitError.RecruitFull)).IsEqualTo(ErrorMessageType.NoErrorMessage);
        await Assert.That(RaidRecruitRules.ToErrorMessage(RaidRecruitError.StaleStamp)).IsEqualTo(ErrorMessageType.NoErrorMessage);
    }
}
