using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>A serving hero as the server tracks them.</summary>
public class HeroRecord
{
    public uint CharacterId { get; init; }
    public uint FactionId { get; init; }
    public byte Grade { get; set; }
    public uint Season { get; init; }
}

/// <summary>
/// Owns who is currently a hero, and publishes that to clients.
/// </summary>
/// <remarks>
/// The election that should fill this does not exist yet - CSHeroVoting and friends are still parse-only
/// stubs - so for now heroes are appointed by GM command. The storage and the packet are the parts an
/// election would reuse unchanged: its whole output is rows in `heroes`.
///
/// Everything hero-gated in the client keys off this list rather than off a per-character flag:
/// X2Hero:IsHero(), the Current Heroes tab, the Hero Missions tab, the Dominion tab and the siege
/// commander button all read GetHeroList(faction), which SCHeroList fills.
/// </remarks>
public class HeroManager : Singleton<HeroManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, HeroRecord> _byCharacter = [];

    public void Load()
    {
        _byCharacter.Clear();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT `character_id`, `faction_id`, `grade`, `season` FROM `heroes`";
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var record = new HeroRecord
            {
                CharacterId = reader.GetUInt32("character_id"),
                FactionId = reader.GetUInt32("faction_id"),
                Grade = reader.GetByte("grade"),
                Season = reader.GetUInt32("season")
            };
            _byCharacter[record.CharacterId] = record;
        }

        Logger.Info("Loaded {0} hero(es)", _byCharacter.Count);
    }

    public bool IsHero(uint characterId) => _byCharacter.ContainsKey(characterId);

    /// <summary>The character's hero_grades rank, or 0 if they are not a hero.</summary>
    /// <remarks>
    /// Grades ascend: 1 Epherium, 2 Delphinad, 3 Ayanad, 4 Erenor, which is the order SlotGrades seats
    /// the pyramid in.
    /// </remarks>
    public byte GradeOf(uint characterId) =>
        _byCharacter.TryGetValue(characterId, out var record) ? record.Grade : (byte)0;

    public IEnumerable<HeroRecord> GetHeroes(uint nationId) =>
        _byCharacter.Values.Where(h => h.FactionId == nationId);

    /// <summary>
    /// Whether a character stands high enough in their nation's ranking to count as a candidate.
    /// </summary>
    /// <remarks>
    /// Candidacy is a position, not a stored flag: hero_conditions.hero_candidate_scope says how many of
    /// the top ranks qualify - 16 in shipped data, which is exactly the cut the Candidates tab draws when
    /// it renders the first 16 rows brown and the rest grey (hero_rank.lua:32).
    ///
    /// Read off the same ranking the tab is sent, so the server's answer and what the player is looking
    /// at cannot drift apart.
    /// </remarks>
    public bool IsCandidate(Character character)
    {
        if (character == null)
            return false;

        var scope = HeroConditions.Current.HeroCandidateScope;
        if (scope <= 0)
            return false;

        return GetRanking(NationOf(character))
            .Take(scope)
            .Any(entry => entry.CharId == character.Id);
    }

    /// <summary>
    /// Top leadership holders in a nation, best first.
    /// </summary>
    /// <remarks>
    /// The database is the base, because a ranking built only from logged-in players would reshuffle
    /// every time someone connected, and the retail window ranks the whole faction.
    ///
    /// Capped at hero_conditions.leadership_ranking_scope - 50 in shipped data - which is the pool the
    /// top candidates are drawn from.
    ///
    /// The caller asks about a NATION, not a faction. The Candidates tab sends the top-level id - 148 is
    /// the Nuia Alliance - while characters carry a member faction such as 103, whose system_factions row
    /// has mother_id 148. Comparing the two directly matches nobody, which is what left the table empty.
    /// So the nation is expanded into its member factions first, mirroring how SystemFaction itself
    /// resolves identity: MotherId when set, otherwise Id.
    ///
    /// Characters on zero leadership are excluded, so an empty ranking stays empty instead of listing the
    /// whole nation tied at nothing.
    ///
    /// Online players are overlaid from memory afterwards. A loaded Character holds the authoritative
    /// leadership and only writes it out on save, so reading the database alone shows a stale ladder:
    /// earn leadership and the ranking does not move until you log out. The overlay also ADDS online
    /// players the query missed, since someone whose stored figure is low - or zero - can be well up the
    /// ladder live.
    /// </remarks>
    public List<HeroRankingEntry> GetRanking(uint nationId)
    {
        var scope = HeroConditions.Current.LeadershipRankingScope;
        if (scope <= 0)
            return [];

        var result = new List<HeroRankingEntry>();

        var nation = (FactionsEnum)nationId;
        var memberFactions = FactionManager.Instance.GetSystemFactions()
            .Where(f => (f.MotherId != FactionsEnum.Invalid ? f.MotherId : f.Id) == nation)
            .Select(f => (uint)f.Id)
            .ToList();

        // A nation with no members still ranks anyone sitting directly on the top-level id.
        if (!memberFactions.Contains(nationId))
            memberFactions.Add(nationId);

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `id`, `leadership_point`, `accumulated_leadership_point`, `expedition_id` " +
                "FROM `characters` " +
                $"WHERE `faction_id` IN ({string.Join(",", memberFactions)}) " +
                "AND `deleted` = 0 AND `leadership_point` > 0 " +
                "ORDER BY `leadership_point` DESC, `id` ASC LIMIT @scope";
            command.Parameters.AddWithValue("@scope", scope);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetUInt32(0);
                var period = reader.GetInt32(1);
                var lifetime = reader.GetInt32(2);
                // The row repeats the header pair: leadership is the lifetime total, score is what was
                // earned this period. The trailing i32 is the expedition, which the tab renders in its
                // own column; 0 reads as "no guild" and the client drops the key.
                result.Add(new HeroRankingEntry(id, lifetime, period, reader.GetInt32(3)));
            }
        }
        catch (Exception ex)
        {
            // An empty list still clears the tab's spinner. Letting this escape would hang the window on
            // top of whatever went wrong in the query.
            Logger.Error(ex, "HeroRanking: failed to load ranking for nation {0}", nationId);
        }

        // Live values win, and online players absent from the query are added.
        var live = new Dictionary<uint, HeroRankingEntry>();
        foreach (var e in result)
            live[(uint)e.CharId] = e;

        foreach (var player in WorldManager.Instance.GetAllCharacters())
        {
            if (!memberFactions.Contains((uint)(player.Faction?.Id ?? 0)) || player.LeadershipPoint <= 0)
                continue;

            live[player.Id] = new HeroRankingEntry(
                player.Id,
                player.AccumulatedLeadershipPoint,
                player.LeadershipPoint,
                (int)(player.Expedition?.Id ?? 0));
        }

        return live.Values
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.CharId)
            .Take(scope)
            .ToList();
    }

    /// <summary>
    /// Appoints a character, or re-grades one who already serves.
    /// </summary>
    /// <remarks>
    /// The nation is resolved from the character's own faction rather than taken as an argument, because
    /// the client asks about nations and a member faction would never match - the same mismatch that
    /// left the ranking empty.
    /// </remarks>
    public void Grant(Character character, byte grade)
    {
        var nation = NationOf(character);
        Seat(character.Id, nation, grade);
        Logger.Info("{0} is now a hero of nation {1} at grade {2}", character.Name, nation, grade);
        Broadcast(nation);
    }

    /// <summary>
    /// Seats one hero by id, without needing them loaded.
    /// </summary>
    /// <remarks>
    /// Split out of Grant because an election seats whoever won, and most winners will be offline when
    /// the count runs. Nothing here reads the character - the nation is supplied by the caller, which
    /// for the count is the nation whose ballot they stood in.
    ///
    /// Does not broadcast: seating six heroes one at a time would push the roster six times. The caller
    /// broadcasts once when the nation is done.
    /// </remarks>
    public void Seat(uint characterId, uint nationId, byte grade)
    {
        var record = new HeroRecord
        {
            CharacterId = characterId,
            FactionId = nationId,
            Grade = grade,
            Season = HeroSeason.CurrentId
        };
        _byCharacter[characterId] = record;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "REPLACE INTO `heroes` (`character_id`,`faction_id`,`grade`,`season`) " +
            "VALUES (@c,@f,@g,@s)";
        command.Parameters.AddWithValue("@c", record.CharacterId);
        command.Parameters.AddWithValue("@f", record.FactionId);
        command.Parameters.AddWithValue("@g", record.Grade);
        command.Parameters.AddWithValue("@s", record.Season);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    /// <summary>Removes every serving hero of a nation, in memory and in the database.</summary>
    /// <remarks>
    /// The outgoing roster has to go before the incoming one is seated, or a hero who was not re-elected
    /// would simply stay - REPLACE INTO only overwrites the winners' own rows.
    /// </remarks>
    public int ClearNation(uint nationId)
    {
        var leaving = _byCharacter.Values.Where(h => h.FactionId == nationId).Select(h => h.CharacterId).ToList();
        foreach (var id in leaving)
            _byCharacter.Remove(id);

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `heroes` WHERE `faction_id` = @f";
        command.Parameters.AddWithValue("@f", nationId);
        command.Prepare();
        command.ExecuteNonQuery();

        return leaving.Count;
    }

    /// <summary>Pushes a nation's roster to everyone in it. Public so the count can announce once.</summary>
    public void BroadcastNation(uint nationId) => Broadcast(nationId);

    public bool Revoke(uint characterId)
    {
        if (!_byCharacter.Remove(characterId, out var record))
            return false;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `heroes` WHERE `character_id` = @c";
        command.Parameters.AddWithValue("@c", characterId);
        command.Prepare();
        command.ExecuteNonQuery();

        Broadcast(record.FactionId);
        return true;
    }

    /// <summary>The nation a character belongs to: the top-level faction, not their member faction.</summary>
    public static uint NationOf(Character character)
    {
        var faction = character.Faction;
        if (faction == null)
            return 0;

        return (uint)(faction.MotherId != FactionsEnum.Invalid ? faction.MotherId : faction.Id);
    }

    /// <summary>
    /// Delivers every copy of a character's leadership the client keeps.
    /// </summary>
    /// <remarks>
    /// The client keeps the figure in two places, written by three packets, and the character sheet's two
    /// leadership rows are built side by side from one each in x2ui/characterinfo/character_info_table.lua:
    ///
    ///   this period, "leadership_point"       -> X2Hero:GetMyScore().score, from SCHeroSeasonInfo
    ///   last period, "last_leadership_point"  -> GetGamePoints().periodLeadershipPointStr, which is
    ///                                            ClientPlayer[0x68] + 0xef0
    ///
    /// Two packets write +0xef0 and they must agree, which is the whole reason this is one method.
    /// SCCharacterGamePoints fills the fourteen-slot money array at ClientPlayer+0xec0 - slot 12 lands on
    /// +0xef0 exactly - and SCHeroSeasonOff's handler (0x108820) stores its score field straight there.
    /// SendLeadership sends the game points first, so whatever SCHeroSeasonOff carries is what the sheet
    /// ends up showing: sending the current total there made BOTH rows read the current total.
    ///
    /// Both therefore carry LeadershipPeriodPoint, and it is genuinely the last period's figure rather
    /// than a copy of the current one: the rating gate (.text 0x164f20, reached from CanAddReputation's
    /// binding at 0x19e4c0) loads +0xef0 at 0x16505f, compares hero_conditions' leadership requirement
    /// against it and returns error 0x3b6 when it falls short - and the "vote available" badge sits on
    /// that same last-period row (common.lua CreateLastSeasonLeadershipPointLabel). Rating and voting
    /// eligibility are earned in the period before the one they are spent in.
    ///
    /// SCHeroSeasonOff's name reads as if it belonged to a closed season, but the handler is
    /// unconditional: it stores the score and raises UI event 0x2bf. Both season packets go out together.
    /// </remarks>
    public static void SendLeadership(Character character)
    {
        character.SendPacket(new SCCharacterGamePointsPacket(character));
        // leadership = lifetime, score = this period. The client renders them as "period/lifetime".
        character.SendPacket(new SCHeroSeasonInfoPacket(
            (int)HeroSeason.CurrentId, character.AccumulatedLeadershipPoint, character.LeadershipPoint));
        character.SendPacket(new SCHeroSeasonOffPacket(
            (int)HeroSeason.CurrentId, character.LeadershipPeriodPoint));
    }

    /// <summary>
    /// SendLeadership, followed by the event that makes the UI re-read what was just sent.
    /// </summary>
    /// <remarks>
    /// Separate from SendLeadership because SCHeroScoreUpdated is addressed by ObjId, which a character
    /// does not have until Spawn(). The login burst has to use the plain send; anything that changes
    /// leadership on a character already in the world wants this one, since none of the leadership
    /// widgets poll - they refresh on HERO_SCORE_UPDATED and nothing else.
    /// </remarks>
    public static void PublishLeadership(Character character)
    {
        SendLeadership(character);
        character.SendPacket(new SCHeroScoreUpdatedPacket(character.ObjId, character.LeadershipPoint));
    }

    /// <summary>Sends a nation's hero state and roster to one client.</summary>
    /// <remarks>
    /// SCHeroEventState goes first because two client features are gated on it, not just display.
    /// The rating gate (.text 0x164f20) checks the hero feature bit and then looks schedule event 1 -
    /// leadership_ranking - up in the hero manager, returning error 0x3ba when it is absent. Without that
    /// the peer-rating button never appears on a target's unit frame at all, so no reputation is earnable.
    ///
    /// The entry's middle field is the SEASON, not the nation. It was the nation here, which looked
    /// harmless because nothing visible depends on it - the Current Heroes tab's faction combobox is
    /// filled from SCHeroList rows, not from this - but it left the leadership_ranking slot pointing at
    /// a hero_schedules row that does not exist, so the rating gate bailed before any of its own checks.
    ///
    /// Which phase is live comes from HeroElectionManager, which follows hero_schedules. It used to be
    /// hardcoded to leadership_ranking and hero_period both running at once, which was enough to make
    /// peer rating work and wrong for everything with a phase of its own - the ballot, the abstain
    /// window, and the "Active Period" headers that read their dates back out of the schedule.
    ///
    /// SCHeroList then fills the manager's hero map at +0xD8, which is what X2Hero:IsHero() reads.
    ///
    /// clearAll resets the client's slots before the entries are applied, so it belongs on a first send
    /// and nowhere else. Sending it on every phase change replayed the whole schedule from empty each
    /// time, which made the client re-detect leadership_ranking as having just ended and repeat its
    /// "Finished collecting Leadership information" announcement on every transition.
    /// </remarks>
    public void Send(Character character, uint nationId = 0, bool clearAll = true, HeroPhase? leaving = null)
    {
        if (nationId == 0)
            nationId = NationOf(character);

        var states = HeroElectionManager.Instance.BuildStates(leaving);
        if (states.Count > 0)
            character.SendPacket(new SCHeroEventStatePacket(clearAll, states));

        // SCHeroVoting is NOT sent here, even though it carries the "already voted" flag the ballot
        // needs. Sending it opens the Hero Vote window - bit 2 is not the only thing that does, whatever
        // .text 0x1085b2 suggested - so putting it in the schedule broadcast threw an unasked-for ballot
        // at every player online the moment hero_voting was entered. It goes out with the ballot
        // instead; see HeroElectionManager.SendBallot.
        character.SendPacket(new SCHeroListPacket((int)nationId, BuildRows(nationId)));

        // A serving hero needs their mobilization counters before the issuance doodad will work; see
        // SCHeroMobilizationOrderUpdated. Sent after the roster, since it describes a hero and the
        // client has only just learned who those are.
        SendMobilizationOrders(character);
    }

    /// <summary>
    /// Sends a hero their mobilization order counters, or nothing at all if they are not a hero.
    /// </summary>
    /// <remarks>
    /// The client will not let a hero press the issuance button unless it has these, so this is what
    /// makes the doodad usable at all rather than merely accurate; see SCHeroMobilizationOrderUpdated.
    /// Sent again after every issue, so the window's "Rem. n/5" and the server's budget stay in step.
    /// </remarks>
    public void SendMobilizationOrders(Character character)
    {
        if (character == null || !IsHero(character.Id))
            return;

        var (today, total) = MobilizationOrderManager.Instance.CountsFor(character.Id);
        character.SendPacket(new SCHeroMobilizationOrderUpdatedPacket(
            character.Id, (int)HeroSeason.CurrentId, today, total));
    }

    /// <summary>
    /// Pushes a nation's hero list to everyone online in it, after the roster changes.
    /// </summary>
    /// <remarks>
    /// Sent to the whole nation, not just the appointee: IsHero() decides what OTHER players see too -
    /// hero-only siege controls, the Dominion tab, the icon beside a name - so a client that never
    /// learns about a new hero keeps showing the old roster until it relogs.
    /// </remarks>
    private void Broadcast(uint nationId)
    {
        foreach (var player in WorldManager.Instance.GetAllCharacters())
        {
            // The roster changed, not the schedule: no leaving phase, so BuildStates re-states only the
            // live one and nothing is announced.
            if (NationOf(player) == nationId)
                Send(player, nationId, clearAll: false);
        }
    }

    /// <summary>
    /// Slot order of the Current Heroes pyramid, by hero_grades row: one Erenor, two Ayanad, three
    /// Delphinad.
    /// </summary>
    /// <remarks>
    /// The client does NOT place heroes by the grade we send. hero_current_status.lua builds six slots
    /// once via CreateItem(grade) where grade IS the slot index, bakes the emblem into the slot, and
    /// then fills purely by position: heroList[i] goes to item[i]. So the order of rows in the packet is
    /// the placement, and this table is what makes a grade-4 hero land in the Erenor slot.
    ///
    /// Grade is still real data - tab_siege_raid_team.lua and faction_relations.lua render a per-hero
    /// grade badge, and TeamOwnerHandoverReason.HigherHeroGrade compares them - it just is not what
    /// positions anyone here.
    /// </remarks>
    private static readonly byte[] SlotGrades = [4, 3, 3, 2, 2, 2];

    /// <summary>
    /// Builds one row per pyramid slot, in slot order, with empty slots padded.
    /// </summary>
    /// <remarks>
    /// Padding is required, not cosmetic. Because placement is positional, packing a sparse roster puts
    /// heroes in the wrong slots: seat grades 4 and 2 with no grade 3, and the grade-2 hero becomes row 2
    /// and renders under an Ayanad emblem. A blank row holds the slot instead - item:SetInfo treats a row
    /// with no name and no nameCacheQueryId as Vacant, which is exactly the placeholder the commented-out
    /// test data in hero_current_status.lua uses.
    /// </remarks>
    private List<HeroListEntry> BuildRows(uint nationId)
    {
        var pools = GetHeroes(nationId)
            .GroupBy(h => h.Grade)
            .ToDictionary(g => g.Key, g => new Queue<HeroRecord>(g.OrderBy(h => h.CharacterId)));

        var rows = new List<HeroListEntry>(SlotGrades.Length);
        for (var slot = 0; slot < SlotGrades.Length; slot++)
        {
            var grade = SlotGrades[slot];
            HeroRecord hero = null;
            if (pools.TryGetValue(grade, out var pool) && pool.Count > 0)
                hero = pool.Dequeue();

            if (hero == null)
            {
                // Vacant: nation only, so the slot still belongs to this faction, and no character.
                rows.Add(new HeroListEntry(0, 0, (int)nationId, 0, 0, 0, 0, grade));
                continue;
            }

            var character = WorldManager.Instance.GetCharacterById(hero.CharacterId);
            // Offline heroes still belong on the list, so their figures come from the database rather
            // than from a loaded character.
            var (period, lifetime, expedition) = character != null
                ? (character.LeadershipPoint, character.AccumulatedLeadershipPoint, (int)(character.Expedition?.Id ?? 0))
                : LoadRow(hero.CharacterId);

            rows.Add(new HeroListEntry(
                Unk0: 0,
                CharId: hero.CharacterId,
                FactionId: (int)nationId,
                ExpeditionId: expedition,
                Ranking: slot + 1,
                Score: period,
                AccumPoint: lifetime,
                Grade: hero.Grade));
        }

        foreach (var (grade, pool) in pools)
        {
            if (pool.Count > 0)
                Logger.Warn("Nation {0} has {1} hero(es) at grade {2} with no pyramid slot; not shown",
                    nationId, pool.Count, grade);
        }

        return rows;
    }

    /// <summary>
    /// Term progress for every hero of a nation, for the Mission Status tab.
    /// </summary>
    /// <remarks>
    /// Ordered the same way BuildRows orders the pyramid, so the tab's rows line up with the Current
    /// Heroes tab. The client sorts on its own ranking field anyway - it takes that from the hero list,
    /// not from here - but sending them in a different order would only make the two disagree on screen
    /// for no reason.
    ///
    /// Mission counts are still reported as zero, because nothing counts a hero mission's completions
    /// across a term yet. Zero is what the tab should show for a hero who has completed none, so it is
    /// honest rather than a placeholder, and it stops being zero when that counter lands.
    /// </remarks>
    public List<HeroScoreEntry> BuildScores(uint nationId)
    {
        var scores = new List<HeroScoreEntry>();

        foreach (var hero in GetHeroes(nationId).OrderByDescending(h => h.Grade).ThenBy(h => h.CharacterId))
        {
            var character = WorldManager.Instance.GetCharacterById(hero.CharacterId);
            var period = character?.LeadershipPoint ?? LoadRow(hero.CharacterId).period;

            scores.Add(new HeroScoreEntry(
                CharacterId: hero.CharacterId,
                Score: period,
                PeriodScore: period,
                MobilizationCount: MobilizationOrderManager.Instance.CountsFor(hero.CharacterId).Total,
                MissionProgress: []));
        }

        return scores;
    }

    private static (int period, int lifetime, int expedition) LoadRow(uint characterId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `leadership_point`, `accumulated_leadership_point`, `expedition_id` " +
                "FROM `characters` WHERE `id` = @c";
            command.Parameters.AddWithValue("@c", characterId);
            command.Prepare();

            using var reader = command.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroManager: failed to read hero row data for character {0}", characterId);
        }

        return (0, 0, 0);
    }
}
