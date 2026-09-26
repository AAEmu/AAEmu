using System.Text;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Models.Game.Team.Recruitment;

public enum RaidRecruitError
{
    None,
    NotFound,
    Expired,
    StaleStamp,
    NotAuthorized,
    AlreadyRecruiting,
    UnknownType,
    UnknownSubType,
    SubTypeNotOfType,
    InvalidHeadcount,
    HeadcountNotAboveMembers,
    LevelBelowSubType,
    LevelAboveCap,
    GearBelowSubType,
    GearAboveCap,
    MessageTooLong,
    InvalidTime,
    NotEnoughMoney,
    OwnRaid,
    ApplicantIsRecruiting,
    Duplicate,
    TooManyApplications,
    RecruitFull,
    ApplicantListFull,
    LevelTooLow,
    GearTooLow,
    Hostile,
    ApplicantsPending,
    ApplicantOffline,
    ApplicantInTeam,
    TeamFull
}

/// <summary>What the manager knows about the acting character's team, flattened so the rules stay value-in, value-out.</summary>
/// <param name="TeamId">0 when the character has no team.</param>
public readonly record struct TeamFacts(uint TeamId, uint OwnerId, ulong OfficerId, bool IsParty, bool ActorIsMember)
{
    public static readonly TeamFacts None = default;
}

/// <summary>Every input the apply gate looks at, gathered by the manager.</summary>
public readonly record struct RaidApplyCheck(
    uint ApplicantId,
    uint OwnerId,
    uint ApplicantTeamId,
    uint RecruitTeamId,
    bool ApplicantIsRecruiting,
    bool AlreadyApplied,
    int PendingApplications,
    int MemberCount,
    uint Headcount,
    int ApplicantCount,
    int ApplicantLevel,
    int ApplicantHeirLevel,
    uint LimitLevel,
    int ApplicantGearScore,
    uint LimitGearPoint,
    RelationState Relation,
    long Stamp,
    long CreateTime,
    long ExpireTime,
    long NowUnix);

/// <summary>
/// The raid recruitment board's decisions, kept apart from the manager so they can be tested without a world.
/// The client enforces most of them in its own UI first (raid_recruit_mgr_view.lua line 390 is the apply
/// gate, dialog.lua the post form); the server repeats them so a modified client gets the same answer.
/// </summary>
public static class RaidRecruitRules
{
    /// <summary>
    /// ui_texts 11291 "Can be applied to up to 3 raids at the same time"; the client's list builder
    /// sets fullApplicant when its own application map holds more than 2.
    /// </summary>
    public const int MaxApplicationsPerCharacter = 3;

    /// <summary>
    /// The client applies a maxApplicantCount of 100, and the SCRaidApplicantList reader
    /// stops at 100 rows.
    /// </summary>
    public const int MaxApplicantsPerRecruitment = 100;

    /// <summary>The SCRaidRecruitList reader stops at 0x32 rows.</summary>
    public const int ListLimit = 50;

    /// <summary>The record serializer reads msg into a 200 byte field.</summary>
    public const int MessageByteLimit = 200;

    /// <summary>ownerName and charName are read into 0x80 byte fields.</summary>
    public const int NameByteLimit = 128;

    /// <summary>
    /// RAID_RECRUIT_EXPIRE_DELAY_MINUTE, registered for Lua as 20 and shown in
    /// ui_texts 8858: posts are auto-deleted "$2 minutes after Departure Time".
    /// </summary>
    public const int ExpireDelayMinutes = 20;

    /// <summary>X2Team:GetRaidRecruitExpense answers 10000 when no expense band covers the delay.</summary>
    public const long DefaultExpense = 10000;

    public static RaidRecruitError ValidatePost(
        in RaidRecruitPostRequest request,
        RaidRecruitContent content,
        int memberCount,
        int levelCap,
        int maxHeirLevel)
    {
        if (!content.Types.TryGetValue(request.TypeId, out var type) || !type.Visible)
            return RaidRecruitError.UnknownType;
        if (!content.SubTypes.TryGetValue(request.SubTypeId, out var subType))
            return RaidRecruitError.UnknownSubType;
        if (subType.TypeId != request.TypeId)
            return RaidRecruitError.SubTypeNotOfType;

        // The dialog offers exactly raid_recruit_headcounts (3, 5, 10, 25, 50) and only enables the post
        // button while the current member count is below the pick (raid_recruit_recruitment_popup.lua,
        // ui_texts 8837).
        if (request.Headcount > int.MaxValue || !content.Headcounts.Contains((int)request.Headcount))
            return RaidRecruitError.InvalidHeadcount;
        if (request.Headcount <= memberCount)
            return RaidRecruitError.HeadcountNotAboveMembers;

        // Level runs from the sub type's floor to the level cap plus the heir levels, the way the dialog's
        // Satisfy() bounds it (levelMin = subType.level, levelMax = levelLimit or MaxHeirLevel offset by
        // MinHeirLevel) and the way the apply gate later compares UnitLevel + UnitHeirLevel against it.
        if (request.LimitLevel < subType.Level)
            return RaidRecruitError.LevelBelowSubType;
        if (request.LimitLevel > levelCap + maxHeirLevel)
            return RaidRecruitError.LevelAboveCap;

        // Gear score runs from the sub type's floor to gearScoreLimitMax, which is content_configs
        // max_gear_score_to_recruit (id 385, 20000). A missing config cannot bound the field, so refuse.
        if (request.LimitGearPoint < subType.GearScore)
            return RaidRecruitError.GearBelowSubType;
        if (content.MaxGearScoreToRecruit <= 0 || request.LimitGearPoint > content.MaxGearScoreToRecruit)
            return RaidRecruitError.GearAboveCap;

        if (request.Message == null || Encoding.UTF8.GetByteCount(request.Message) > MessageByteLimit)
            return RaidRecruitError.MessageTooLong;

        // Departure is a clock time; the dialog offers 0..23 hours in ten minute steps.
        if (request.Hour > 23 || request.Minute > 59)
            return RaidRecruitError.InvalidTime;

        return RaidRecruitError.None;
    }

    /// <summary>
    /// The next occurrence of hour:minute on the clock, one day out when it has already passed. Mirrors
    /// GetRaidRecruitExpense, which builds the tm from local time, replaces hour and minute
    /// and adds 86400 s when hour &lt; now.hour or (hour == now.hour and minute &lt;= now.minute).
    /// </summary>
    public static DateTimeOffset Departure(DateTimeOffset now, uint hour, uint minute)
    {
        var departure = new DateTimeOffset(now.Year, now.Month, now.Day, (int)hour, (int)minute, now.Second, now.Offset);
        if (hour < now.Hour || (hour == now.Hour && minute <= now.Minute))
            departure = departure.AddDays(1);
        return departure;
    }

    /// <summary>Whole hours until departure, truncated the way the client's integer division by 3600 does.</summary>
    public static int HoursUntil(DateTimeOffset departure, DateTimeOffset now)
    {
        var seconds = (long)(departure - now).TotalSeconds;
        return seconds <= 0 ? 0 : (int)(seconds / 3600);
    }

    /// <summary>
    /// raid_recruit_time_and_expenses: the band whose min..max covers the whole hours until departure
    /// (1-6, 7-10, 11-18, 19-24, each 15000 copper). Outside every band the client shows 10000, so the
    /// server charges the same.
    /// </summary>
    public static long ExpenseFor(int hoursUntilDeparture, IReadOnlyList<RaidRecruitExpenseBand> bands)
    {
        foreach (var band in bands)
            if (band.MinHour <= hoursUntilDeparture && hoursUntilDeparture <= band.MaxHour)
                return band.Expense;
        return DefaultExpense;
    }

    public static long ExpireUnixTime(DateTimeOffset departure) =>
        departure.ToUnixTimeSeconds() + ExpireDelayMinutes * 60L;

    public static bool IsExpired(long expireUnixTime, long nowUnixTime) => nowUnixTime >= expireUnixTime;

    /// <summary>The same right TeamManager.CanInvite grants: the owner, or an officer of a raid.</summary>
    public static bool CanInvite(uint actorId, in TeamFacts team) =>
        team.TeamId != 0 && team.ActorIsMember &&
        (team.OwnerId == actorId || (!team.IsParty && team.OfficerId == actorId));

    /// <summary>Who may post: a solo character, a team's owner, or a raid's officer.</summary>
    public static bool CanPost(uint actorId, in TeamFacts team) => team.TeamId == 0 || CanInvite(actorId, team);

    /// <summary>Who may accept, reject, delete or change the option: the poster, or anyone who could invite into the recruiting team.</summary>
    public static bool CanManage(uint actorId, uint recruitOwnerId, uint recruitTeamId, in TeamFacts actorTeam) =>
        actorId == recruitOwnerId ||
        (recruitTeamId != 0 && actorTeam.TeamId == recruitTeamId && CanInvite(actorId, actorTeam));

    /// <summary>
    /// The applicant window is open to every member of the recruiting team (the client only disables the
    /// auto join radio for members who are neither owner nor officer, raid_recruit_applicant_list.lua).
    /// </summary>
    public static bool CanViewApplicants(uint actorId, uint recruitOwnerId, uint recruitTeamId, in TeamFacts actorTeam) =>
        CanManage(actorId, recruitOwnerId, recruitTeamId, actorTeam) ||
        (recruitTeamId != 0 && actorTeam.TeamId == recruitTeamId && actorTeam.ActorIsMember);

    /// <summary>ui_texts 8829: the applicant list must be empty to switch to auto join.</summary>
    public static bool CanEnableAutoJoin(int applicantCount) => applicantCount == 0;

    /// <summary>
    /// A post can only recruit for a team its poster holds invite rights on, the way manual accept seats
    /// through CanInvite; anything else is the TEAM_FULL a plain member's post would hit.
    /// </summary>
    public static bool CanRecruitFor(uint posterId, in TeamFacts team) =>
        team.TeamId != 0 && team.ActorIsMember && CanInvite(posterId, team);

    /// <summary>Whether the team has room under the post's headcount for one more applicant.</summary>
    public static bool HasSeatFor(int memberCount, uint headcount, int memberLimit) =>
        memberCount < Math.Min((long)headcount, memberLimit);

    /// <summary>The client's apply gate (raid_recruit_mgr_view.lua line 390) plus the checks its bindings make.</summary>
    public static RaidRecruitError CanApply(in RaidApplyCheck check)
    {
        if (IsExpired(check.ExpireTime, check.NowUnix))
            return RaidRecruitError.Expired;
        // The client passes the row's createTime back as the stamp; a different one means a stale list.
        if (check.Stamp != check.CreateTime)
            return RaidRecruitError.StaleStamp;
        // RAID_CANTNOT_APPLY_MY_RAID (1012): own post, or the post of the team one already belongs to.
        if (check.ApplicantId == check.OwnerId ||
            (check.RecruitTeamId != 0 && check.ApplicantTeamId == check.RecruitTeamId))
            return RaidRecruitError.OwnRaid;
        // NOT_JOIN_RAID (1002): the client refuses a detail while its own team is recruiting.
        if (check.ApplicantIsRecruiting)
            return RaidRecruitError.ApplicantIsRecruiting;
        // RAID_APPLY_IS_DUPLICATE (1013).
        if (check.AlreadyApplied)
            return RaidRecruitError.Duplicate;
        if (check.PendingApplications >= MaxApplicationsPerCharacter)
            return RaidRecruitError.TooManyApplications;
        if (check.MemberCount >= check.Headcount)
            return RaidRecruitError.RecruitFull;
        if (check.ApplicantCount >= MaxApplicantsPerRecruitment)
            return RaidRecruitError.ApplicantListFull;
        // RAID_CANTNOT_REQUIRED_LEVEL (1016); the gate compares UnitLevel + UnitHeirLevel.
        if (check.ApplicantLevel + check.ApplicantHeirLevel < check.LimitLevel)
            return RaidRecruitError.LevelTooLow;
        if (check.ApplicantGearScore < check.LimitGearPoint)
            return RaidRecruitError.GearTooLow;
        // Team invitations already refuse hostile players (TeamManager.AskToJoin); a recruit is one.
        if (check.Relation == RelationState.Hostile)
            return RaidRecruitError.Hostile;
        return RaidRecruitError.None;
    }

    /// <summary>
    /// Seats still open when accepting: the post's headcount and the raid's fifty slots both bind, and an
    /// accepted applicant who has not answered yet holds one.
    /// </summary>
    public static int OpenSeats(int memberCount, int acceptedPending, uint headcount, int memberLimit)
    {
        var cap = Math.Min((long)headcount, memberLimit);
        return (int)Math.Max(0, cap - memberCount - acceptedPending);
    }

    /// <summary>The role radio sends TMROLE_NONE..TMROLE_RANGED_DEALER (0..4); anything else is undecided.</summary>
    public static MemberRole ToMemberRole(uint role) =>
        role <= (uint)MemberRole.RangedAttacker ? (MemberRole)role : MemberRole.Undecided;

    /// <summary>The client's own reason codes for the refusals it has a string for; none for the rest.</summary>
    public static ErrorMessageType ToErrorMessage(RaidRecruitError error) => error switch
    {
        RaidRecruitError.OwnRaid => ErrorMessageType.RaidCannotApplyMyRaid,
        RaidRecruitError.ApplicantIsRecruiting => ErrorMessageType.NotJoinRaid,
        RaidRecruitError.Duplicate => ErrorMessageType.RaidApplyIsDuplicate,
        RaidRecruitError.LevelTooLow => ErrorMessageType.RaidCannotRequiredLevel,
        RaidRecruitError.GearTooLow => ErrorMessageType.NotEnoughGearScore,
        RaidRecruitError.Hostile => ErrorMessageType.TeamInviteRefused,
        RaidRecruitError.NotAuthorized => ErrorMessageType.TeamNoRights,
        RaidRecruitError.TeamFull => ErrorMessageType.TeamFull,
        RaidRecruitError.ApplicantInTeam => ErrorMessageType.TeamInviteeInTeam,
        RaidRecruitError.ApplicantOffline => ErrorMessageType.TeamInviteeOffline,
        _ => ErrorMessageType.NoErrorMessage
    };
}
