using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Team.Recruitment;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The raid recruitment board (the Raid Recruit/Search window, ui_texts 8816). Posts live only in memory:
/// the client's own tooltip (ui_texts 8858) says a post is deleted when its recruiter logs off or returns to
/// character select, 20 minutes after departure, or on a faction change, and the teams they recruit for are
/// themselves in-memory TeamManager state, so there is nothing to persist. One post per recruiter and per
/// team; the poster's character id is the post's key on the wire.
/// </summary>
public class RaidRecruitmentManager(ITickManager tickManager) : Singleton<RaidRecruitmentManager>, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private readonly Dictionary<uint, RaidRecruitment> _byOwner = [];
    // applicant id -> the owner ids of the posts they applied to (at most MaxApplicationsPerCharacter)
    private readonly Dictionary<uint, HashSet<uint>> _applications = [];

    /// <summary>Singleton fallback when no container is configured (unit tests); nothing is scheduled then.</summary>
    public RaidRecruitmentManager() : this(null)
    {
    }

    public void Load() => tickManager?.OnTick.Subscribe(Sweep, SweepInterval);

    public bool IsRecruiting(uint characterId)
    {
        lock (_lock)
        {
            if (_byOwner.ContainsKey(characterId))
                return true;
            var team = TeamManager.Instance.GetActiveTeamByUnit(characterId);
            return team != null && FindByTeam(team.Id) != null;
        }
    }

    // CSRaidRecruitList: the whole board minus posts of hostile recruiters, in posting order. The client
    // reads at most 50 rows; totalCount still reports every visible post.
    public void List(Character viewer)
    {
        if (viewer == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var records = new List<RaidRecruitRecord>();
            foreach (var entry in _byOwner.Values.OrderBy(e => e.CreateTime).ThenBy(e => e.OwnerId))
            {
                if (viewer.GetRelationStateTo(entry.Owner) == Models.Game.Faction.RelationState.Hostile)
                    continue;
                records.Add(entry.ToRecord(MemberCount(entry)));
            }

            viewer.SendPacket(new SCRaidRecruitListPacket(records.Count,
                records.Count > RaidRecruitRules.ListLimit ? records.GetRange(0, RaidRecruitRules.ListLimit) : records));
        }
    }

    // CSRaidRecruitDetail: the client sends the row's createTime back (its serializer names the field
    // expireTime, its Lua passes createTime), so a stale row gets no answer.
    public void Detail(Character viewer, ulong ownerId, long stamp)
    {
        if (viewer == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var entry = Find(ownerId);
            if (entry == null || entry.CreateTime != stamp)
            {
                Logger.Debug("Raid recruit detail {0} stamp {1} not found for {2}", ownerId, stamp, viewer.Name);
                return;
            }

            if (viewer.GetRelationStateTo(entry.Owner) == Models.Game.Faction.RelationState.Hostile)
                return;

            viewer.SendPacket(new SCRaidRecruitDetailPacket(entry.ToRecord(MemberCount(entry))));
        }
    }

    // CSRaidRecruitAdd.
    public void Post(Character poster, in RaidRecruitPostRequest request)
    {
        if (poster == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var facts = FactsFor(poster, out var team);
            if (_byOwner.ContainsKey(poster.Id) || (team != null && FindByTeam(team.Id) != null))
            {
                Refuse(poster, RaidRecruitError.AlreadyRecruiting);
                return;
            }

            if (!RaidRecruitRules.CanPost(poster.Id, facts))
            {
                Refuse(poster, RaidRecruitError.NotAuthorized);
                return;
            }

            var content = RaidRecruitGameData.Instance.Content;
            var memberCount = team?.MembersCount() ?? 1;
            var error = RaidRecruitRules.ValidatePost(request, content, memberCount,
                AppConfiguration.Instance.World.PlayerLevelCap, HeirGameData.Instance.MaxLevel);
            if (error != RaidRecruitError.None)
            {
                Refuse(poster, error);
                return;
            }

            // The promotion fee (ui_texts 8835) is what X2Team:GetRaidRecruitExpense showed for this
            // departure, charged the way the guild board charges its posting fee.
            var now = DateTimeOffset.Now;
            var departure = RaidRecruitRules.Departure(now, request.Hour, request.Minute);
            var expense = RaidRecruitRules.ExpenseFor(RaidRecruitRules.HoursUntil(departure, now), content.ExpenseBands);
            if (expense > 0 && !poster.SubtractMoney(SlotType.Inventory, expense, ItemTaskType.RecruitmentDecMoney))
            {
                Refuse(poster, RaidRecruitError.NotEnoughMoney);
                return;
            }

            var entry = new RaidRecruitment
            {
                Owner = poster,
                TeamId = team?.Id ?? 0,
                TypeId = request.TypeId,
                SubTypeId = request.SubTypeId,
                Headcount = request.Headcount,
                LimitLevel = request.LimitLevel,
                LimitGearPoint = request.LimitGearPoint,
                AutoJoin = request.AutoJoin,
                Message = request.Message,
                Hour = request.Hour,
                Minute = request.Minute,
                CreateTime = now.ToUnixTimeSeconds(),
                ExpireTime = RaidRecruitRules.ExpireUnixTime(departure)
            };
            _byOwner[poster.Id] = entry;
            SendToRecruiters(entry, new SCRaidRecruitAddPacket(entry.ToRecord(memberCount)));
        }
    }

    // CSRaidRecruitDel has no body: the poster deletes their own post.
    public void Delete(Character poster)
    {
        if (poster == null)
            return;
        lock (_lock)
        {
            if (_byOwner.TryGetValue(poster.Id, out var entry))
                Remove(entry, notify: true);
        }
    }

    // CSRaidRecruitOption: the applicant window's auto join radio, for the post the actor manages.
    public void SetOption(Character actor, bool autoJoin)
    {
        if (actor == null)
            return;
        lock (_lock)
        {
            var facts = FactsFor(actor, out var team);
            var entry = _byOwner.GetValueOrDefault(actor.Id) ?? (team != null ? FindByTeam(team.Id) : null);
            if (entry == null)
                return;
            if (!RaidRecruitRules.CanManage(actor.Id, entry.OwnerId, entry.TeamId, facts))
            {
                Refuse(actor, RaidRecruitError.NotAuthorized);
                return;
            }

            if (autoJoin && !entry.AutoJoin && !RaidRecruitRules.CanEnableAutoJoin(entry.Applicants.Count))
            {
                Refuse(actor, RaidRecruitError.ApplicantsPending);
                return;
            }

            entry.AutoJoin = autoJoin;
            SendToRecruiters(entry, new SCRaidRecruitOptionPacket(autoJoin));
        }
    }

    // CSRaidApplicantAdd.
    public void Apply(Character applicant, ulong ownerId, uint role, long stamp)
    {
        if (applicant == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var entry = Find(ownerId);
            if (entry == null)
            {
                Refuse(applicant, RaidRecruitError.NotFound);
                return;
            }

            FactsFor(applicant, out var applicantTeam);
            var check = new RaidApplyCheck(
                applicant.Id,
                entry.OwnerId,
                applicantTeam?.Id ?? 0,
                entry.TeamId,
                _byOwner.ContainsKey(applicant.Id) || (applicantTeam != null && FindByTeam(applicantTeam.Id) != null),
                entry.FindApplicant(applicant.Id) != null,
                _applications.GetValueOrDefault(applicant.Id)?.Count ?? 0,
                MemberCount(entry),
                entry.Headcount,
                entry.Applicants.Count,
                applicant.Level,
                applicant.HeirLevel,
                entry.LimitLevel,
                applicant.GearScore,
                entry.LimitGearPoint,
                applicant.GetRelationStateTo(entry.Owner),
                stamp,
                entry.CreateTime,
                entry.ExpireTime,
                Helpers.UnixTimeNow());
            var error = RaidRecruitRules.CanApply(check);
            if (error != RaidRecruitError.None)
            {
                Refuse(applicant, error);
                return;
            }

            var application = new RaidApplicant(applicant, role, Helpers.UnixTimeNow());
            entry.Applicants.Add(application);
            if (!_applications.TryGetValue(applicant.Id, out var owners))
                _applications[applicant.Id] = owners = [];
            owners.Add(entry.OwnerId);
            applicant.SendPacket(new SCRaidApplicantAddPacket(entry.ToRecord(MemberCount(entry))));

            // Auto-Invite (ui_texts 8827) approves on the spot; the applicant still confirms through the
            // accept popup, which is the only path that seats anyone.
            if (entry.AutoJoin)
                Approve(entry, application);
        }
    }

    // CSRaidApplicantDel: the applicant withdraws.
    public void Withdraw(Character applicant, ulong ownerId)
    {
        if (applicant == null)
            return;
        lock (_lock)
        {
            var entry = Find(ownerId);
            var application = entry?.FindApplicant(applicant.Id);
            if (application == null)
                return;
            RemoveApplication(entry, application);
            applicant.SendPacket(new SCRaidApplicantDelPacket(entry.OwnerId));
        }
    }

    // CSRaidApplicantList: the client sends its team owner's id (or its own), so resolve whichever post the
    // actor may look at; a request that reaches no viewable post gets no reply, because the client's
    // RAID_APPLICANT_LIST handler indexes its own recruit record and would have none.
    public void ListApplicants(Character actor, bool subRecruiter, ulong ownerId)
    {
        if (actor == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var facts = FactsFor(actor, out var team);
            var entry = ResolvePost(actor, facts, team, ownerId, view: true);
            if (entry == null)
            {
                Logger.Debug("Raid recruit applicant list for {0} (owner {1}, sub {2}) has no viewable post", actor.Name, ownerId, subRecruiter);
                return;
            }

            var rows = new List<RaidApplicantRecord>(Math.Min(entry.Applicants.Count, RaidRecruitRules.MaxApplicantsPerRecruitment));
            foreach (var application in entry.Applicants)
            {
                if (rows.Count >= RaidRecruitRules.MaxApplicantsPerRecruitment)
                    break;
                rows.Add(application.ToRecord());
            }

            actor.SendPacket(new SCRaidApplicantListPacket(rows));
        }
    }

    // CSRaidApplicantAccept: approve the ticked applicants, as many as there are seats.
    public void Accept(Character actor, ulong ownerId, IReadOnlyList<ulong> characterIds)
    {
        if (actor == null || characterIds == null)
            return;
        lock (_lock)
        {
            SweepExpired();
            var facts = FactsFor(actor, out var team);
            var entry = ResolvePost(actor, facts, team, ownerId, view: false);
            if (entry == null)
            {
                Refuse(actor, RaidRecruitError.NotAuthorized);
                return;
            }

            var seats = RaidRecruitRules.OpenSeats(MemberCount(entry), entry.AcceptedPendingCount, entry.Headcount, Team.RaidMemberLimit);
            foreach (var characterId in characterIds.Distinct())
            {
                if (characterId > uint.MaxValue)
                    continue;
                var application = entry.FindApplicant((uint)characterId);
                if (application == null || application.State == RaidApplicantState.Accepted)
                    continue;
                if (!application.Character.IsOnline)
                {
                    RemoveApplication(entry, application);
                    continue;
                }

                if (seats <= 0)
                {
                    Refuse(actor, RaidRecruitError.TeamFull);
                    break;
                }

                Approve(entry, application);
                seats--;
            }
        }
    }

    // CSRaidApplicantReject.
    public void Reject(Character actor, ulong ownerId, IReadOnlyList<ulong> characterIds)
    {
        if (actor == null || characterIds == null)
            return;
        lock (_lock)
        {
            var facts = FactsFor(actor, out var team);
            var entry = ResolvePost(actor, facts, team, ownerId, view: false);
            if (entry == null)
            {
                Refuse(actor, RaidRecruitError.NotAuthorized);
                return;
            }

            foreach (var characterId in characterIds.Distinct())
            {
                if (characterId > uint.MaxValue)
                    continue;
                var application = entry.FindApplicant((uint)characterId);
                if (application == null)
                    continue;
                RemoveApplication(entry, application);
                if (application.Character.IsOnline)
                    application.Character.SendPacket(new SCRaidApplicantRejectPacket(entry.OwnerId));
            }
        }
    }

    // CSRaidApplicantAcceptReply: the applicant's answer to SCRaidApplicantAccept. Only a join here seats
    // anyone; the popup replies join=false by itself when it times out after 60 s.
    public void AcceptReply(Character applicant, ulong ownerId, bool join, uint role)
    {
        if (applicant == null)
            return;
        lock (_lock)
        {
            var entry = Find(ownerId);
            var application = entry?.FindApplicant(applicant.Id);
            if (application == null || application.State != RaidApplicantState.Accepted)
            {
                Logger.Debug("Raid recruit reply from {0} to {1} without a pending accept", applicant.Name, ownerId);
                return;
            }

            if (!join)
            {
                RemoveApplication(entry, application);
                applicant.SendPacket(new SCRaidApplicantDelPacket(entry.OwnerId));
                return;
            }

            if (!entry.Owner.IsOnline)
            {
                RemoveApplication(entry, application);
                applicant.SendErrorMessage(ErrorMessageType.TeamInvitorOffline);
                return;
            }

            if (TeamManager.Instance.GetActiveTeamByUnit(applicant.Id) != null)
            {
                RemoveApplication(entry, application);
                Refuse(applicant, RaidRecruitError.ApplicantInTeam);
                return;
            }

            // This applicant's own accepted seat is the one being taken, so it does not count against them.
            var seats = RaidRecruitRules.OpenSeats(MemberCount(entry), entry.AcceptedPendingCount - 1, entry.Headcount, Team.RaidMemberLimit);
            if (seats <= 0 || !TeamManager.Instance.TryAddRecruitedMember(entry.Owner, applicant, RaidRecruitRules.ToMemberRole(role)))
            {
                RemoveApplication(entry, application);
                Refuse(applicant, RaidRecruitError.TeamFull);
                return;
            }

            entry.TeamId = TeamManager.Instance.GetActiveTeamByUnit(entry.Owner.Id)?.Id ?? entry.TeamId;
            // A seated applicant is done applying anywhere; drop every application they still hold.
            ClearApplications(applicant, notify: true);
            applicant.SendPacket(new SCRaidRecruitAddPacket(entry.ToRecord(MemberCount(entry))));
        }
    }

    /// <summary>ui_texts 8858: logging off or leaving to character select deletes the recruiter's post.</summary>
    public void OnCharacterOffline(Character character)
    {
        if (character == null)
            return;
        lock (_lock)
        {
            if (_byOwner.TryGetValue(character.Id, out var entry))
                Remove(entry, notify: true);
            ClearApplications(character, notify: false);
        }
    }

    /// <summary>ui_texts 8858: changing factions deletes the post.</summary>
    public void OnFactionChanged(Character character)
    {
        if (character == null)
            return;
        lock (_lock)
        {
            if (_byOwner.TryGetValue(character.Id, out var entry))
                Remove(entry, notify: true);
        }
    }

    public void OnTeamDisbanded(Team team)
    {
        if (team == null)
            return;
        lock (_lock)
        {
            var entry = FindByTeam(team.Id);
            if (entry != null)
                Remove(entry, notify: true);
        }
    }

    /// <summary>The post recruits for the team the poster was in; once they leave it, it is gone.</summary>
    public void OnMemberLeft(Team team, uint characterId)
    {
        if (team == null)
            return;
        lock (_lock)
        {
            var entry = FindByTeam(team.Id);
            if (entry != null && entry.OwnerId == characterId)
                Remove(entry, notify: true);
        }
    }

    private void Sweep(TimeSpan _)
    {
        lock (_lock)
        {
            SweepExpired();
        }
    }

    private void SweepExpired()
    {
        var now = Helpers.UnixTimeNow();
        List<RaidRecruitment> dead = null;
        foreach (var entry in _byOwner.Values)
        {
            var teamGone = entry.TeamId != 0 && TeamManager.Instance.GetActiveTeam(entry.TeamId) == null;
            if (RaidRecruitRules.IsExpired(entry.ExpireTime, now) || !entry.Owner.IsOnline || teamGone)
                (dead ??= []).Add(entry);
        }

        if (dead == null)
            return;
        foreach (var entry in dead)
            Remove(entry, notify: true);
    }

    private void Approve(RaidRecruitment entry, RaidApplicant application)
    {
        application.State = RaidApplicantState.Accepted;
        application.Character.SendPacket(new SCRaidApplicantAcceptPacket(entry.OwnerId, application.Role));
    }

    private void Remove(RaidRecruitment entry, bool notify)
    {
        _byOwner.Remove(entry.OwnerId);
        foreach (var application in entry.Applicants)
        {
            if (_applications.TryGetValue(application.CharacterId, out var owners))
            {
                owners.Remove(entry.OwnerId);
                if (owners.Count == 0)
                    _applications.Remove(application.CharacterId);
            }

            if (notify && application.Character.IsOnline)
            {
                application.Character.SendPacket(new SCRaidRecruitDelPacket(entry.OwnerId));
                application.Character.SendPacket(new SCRaidApplicantDelPacket(entry.OwnerId));
            }
        }

        entry.Applicants.Clear();
        if (notify)
            SendToRecruiters(entry, new SCRaidRecruitDelPacket(entry.OwnerId));
    }

    private void RemoveApplication(RaidRecruitment entry, RaidApplicant application)
    {
        entry.Applicants.Remove(application);
        if (!_applications.TryGetValue(application.CharacterId, out var owners))
            return;
        owners.Remove(entry.OwnerId);
        if (owners.Count == 0)
            _applications.Remove(application.CharacterId);
    }

    private void ClearApplications(Character applicant, bool notify)
    {
        if (!_applications.TryGetValue(applicant.Id, out var owners))
            return;
        foreach (var ownerId in owners.ToArray())
        {
            if (_byOwner.TryGetValue(ownerId, out var entry))
            {
                var application = entry.FindApplicant(applicant.Id);
                if (application != null)
                    entry.Applicants.Remove(application);
            }

            if (notify && applicant.IsOnline)
                applicant.SendPacket(new SCRaidApplicantDelPacket(ownerId));
        }

        _applications.Remove(applicant.Id);
    }

    private RaidRecruitment Find(ulong ownerId) =>
        ownerId <= uint.MaxValue && _byOwner.TryGetValue((uint)ownerId, out var entry) ? entry : null;

    private RaidRecruitment FindByTeam(uint teamId)
    {
        if (teamId == 0)
            return null;
        foreach (var entry in _byOwner.Values)
            if (entry.TeamId == teamId)
                return entry;
        return null;
    }

    // The post the client means: the id it sent, else the actor's own, else the actor's team's.
    private RaidRecruitment ResolvePost(Character actor, in TeamFacts facts, Team team, ulong ownerId, bool view)
    {
        foreach (var candidate in new[] { Find(ownerId), _byOwner.GetValueOrDefault(actor.Id), team != null ? FindByTeam(team.Id) : null })
        {
            if (candidate == null)
                continue;
            var allowed = view
                ? RaidRecruitRules.CanViewApplicants(actor.Id, candidate.OwnerId, candidate.TeamId, facts)
                : RaidRecruitRules.CanManage(actor.Id, candidate.OwnerId, candidate.TeamId, facts);
            if (allowed)
                return candidate;
        }

        return null;
    }

    private static TeamFacts FactsFor(Character character, out Team team)
    {
        team = TeamManager.Instance.GetActiveTeamByUnit(character.Id);
        return team == null ? TeamFacts.None : new TeamFacts(team.Id, team.OwnerId, team.OfficerId, team.IsParty, true);
    }

    private static int MemberCount(RaidRecruitment entry) =>
        entry.TeamId == 0 ? 1 : TeamManager.Instance.GetActiveTeam(entry.TeamId)?.MembersCount() ?? 1;

    private static void SendToRecruiters(RaidRecruitment entry, GamePacket packet)
    {
        var team = entry.TeamId == 0 ? null : TeamManager.Instance.GetActiveTeam(entry.TeamId);
        if (team != null)
            team.BroadcastPacket(packet);
        else if (entry.Owner.IsOnline)
            entry.Owner.SendPacket(packet);
    }

    private static void Refuse(Character character, RaidRecruitError error)
    {
        var message = RaidRecruitRules.ToErrorMessage(error);
        if (message != ErrorMessageType.NoErrorMessage)
            character.SendErrorMessage(message);
        Logger.Debug("Raid recruit: {0} refused for {1}", error, character.Name);
    }
}
