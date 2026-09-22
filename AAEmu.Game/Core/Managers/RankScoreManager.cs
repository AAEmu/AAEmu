using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.RankTask;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Scores the ranking boards and keeps them: what a holder is worth, in which window, and where that puts
/// them. A board shows every holder on the server rather than the ones in world, so the figures are written
/// to the database and read back when the window asks.
/// </summary>
/// <remarks>
/// The boards are refreshed on their own hourly tick â€” the cadence the window itself states ("Refreshed
/// every 1 h") â€” while a character's running totals are written with the character, so nothing earned
/// between two ticks is lost.
/// </remarks>
public class RankScoreManager(IRankScoreStore store, ITaskManager taskManager) : Singleton<RankScoreManager>, IInitializable
{
    /// <summary>How often every board is rebuilt, matching the cadence the window shows.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    /// <summary>How long after a start the first rebuild runs, so a restarted server has boards.</summary>
    public static readonly TimeSpan FirstRefreshDelay = TimeSpan.FromMinutes(1);

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Initialize()
    {
        taskManager.Schedule(new RankRefreshTask(), FirstRefreshDelay, RefreshInterval);
        Logger.Info("Rankings: boards refresh every {0}", RefreshInterval);
    }

    /// <summary>
    /// Rebuilds every board: a period board from the running totals everyone has stored, a board over a
    /// figure held right now from the characters in world, whose equipment is what it is read from.
    /// </summary>
    public int Refresh(IReadOnlyList<Character> charactersInWorld)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var written = Refresh(charactersInWorld, connection, transaction);
        transaction.Commit();
        Logger.Info("Rankings: refreshed {0} board line(s)", written);
        return written;
    }

    /// <summary>The same rebuild on a caller's connection and transaction.</summary>
    public int Refresh(IReadOnlyList<Character> charactersInWorld, MySqlConnection connection, MySqlTransaction transaction)
    {
        var now = DateTime.UtcNow;
        var written = 0;

        written += PayEndedWindows(now, connection, transaction);

        foreach (var board in RankingGameData.Instance.Ranks)
        {
            if (RankingGameData.Instance.HolderKindOf(board) != RankHolderKind.Character)
                continue;

            var period = RankingGameData.Instance.PeriodFor(board, now).StartUtc;
            var rows = new List<RankScore>();

            if (RankingGameData.Instance.GamePointCounterOf(board) is { } counter)
            {
                // Everyone with a total in this window is on the board, whether they are in world or not.
                rows.AddRange(store.ReadGamePointBoard(board.Id, counter.Kind, counter.Method, period));
            }
            else if (RankRecordRules.ForBoardKind(board.KindId) is { } recordKind)
            {
                // A board over what a character did is read the same way: everyone with a record in this
                // window is on it, whether they are in world or not.
                rows.AddRange(store.ReadRecordBoard(board.Id, recordKind, period));
            }
            else
            {
                var gate = RankingGameData.Instance.GateFor(board.Id);
                foreach (var character in charactersInWorld ?? [])
                {
                    var line = CharacterScore(character, board, gate);
                    if (line == null)
                        continue;

                    rows.Add(new RankScore
                    {
                        RankId = board.Id,
                        HolderKind = RankHolderKind.Character,
                        HolderId = character.Id,
                        AccountId = character.AccountId,
                        WorldId = (byte)AppConfiguration.Instance.Id,
                        Value = line.Value.Value,
                        BareValue = line.Value.BareValue,
                        SubData = line.Value.SubData?.ToBytes(),
                        PeriodStartUtc = period,
                        UpdatedAtUtc = now
                    });
                }
            }

            if (rows.Count == 0)
                continue;

            store.Save(connection, transaction, rows);
            written += rows.Count;
        }

        written += RefreshExpeditionBoards(now, connection, transaction, charactersInWorld, null);

        return written;
    }

    /// <summary>
    /// Rebuilds the boards an expedition holds rather than a character. Only the guild level board has a
    /// source the World owns: the expedition's level, and the equipment points of its members â€” which is
    /// the value the character gear board keeps for every character, so an offline member still counts.
    /// A board whose figure nothing produces is left as it is rather than given a made-up one.
    /// </summary>
    /// <param name="expeditions">
    /// The guilds to build the boards from, or null to take the ones the server holds. A server with no
    /// board over expeditions never asks for them.
    /// </param>
    internal int RefreshExpeditionBoards(DateTime nowUtc, MySqlConnection connection, MySqlTransaction transaction,
        IReadOnlyList<Character> charactersInWorld, IReadOnlyList<Expedition> expeditions)
    {
        var boards = RankingGameData.Instance.Ranks
            .Where(board => RankingGameData.Instance.HolderKindOf(board) == RankHolderKind.Expedition
                            && board.KindId == RankingGameData.ExpeditionGearScoreKind)
            .ToList();
        if (boards.Count == 0)
            return 0;

        expeditions ??= Expeditions();
        if (expeditions.Count == 0)
            return 0;

        // The members' equipment points are what the character gear board already holds.
        var gearBoard = RankingGameData.Instance.Ranks
            .FirstOrDefault(board => board.DetailType == RankingGameData.GearScoreDetailType);
        if (gearBoard == null)
            return 0;

        var memberIds = expeditions
            .SelectMany(expedition => expedition.Members)
            .Select(member => (ulong)member.CharacterId)
            .Distinct()
            .ToList();
        var kept = store.ReadValues(gearBoard.Id, RankingGameData.Instance.PeriodFor(gearBoard, nowUtc).StartUtc, memberIds);
        var live = (charactersInWorld ?? [])
            .GroupBy(character => character.Id)
            .ToDictionary(group => group.Key, group => (long)group.First().GearScore);

        var written = 0;
        foreach (var board in boards)
        {
            var period = RankingGameData.Instance.PeriodFor(board, nowUtc).StartUtc;
            var rows = new List<RankScore>();

            foreach (var expedition in expeditions)
            {
                var total = 0L;
                foreach (var member in expedition.Members)
                {
                    if (live.TryGetValue(member.CharacterId, out var inWorld))
                        total += inWorld;
                    else if (kept.TryGetValue(member.CharacterId, out var stored))
                        total += stored;
                }

                rows.Add(new RankScore
                {
                    RankId = board.Id,
                    HolderKind = RankHolderKind.Expedition,
                    HolderId = (ulong)expedition.Id,
                    WorldId = (byte)AppConfiguration.Instance.Id,
                    Value = expedition.Level,
                    BareValue = total,
                    SubData = RankingSubData.ForOneCount(expedition.Members.Count).ToBytes(),
                    PeriodStartUtc = period,
                    UpdatedAtUtc = nowUtc
                });
            }

            store.Save(connection, transaction, rows);
            written += rows.Count;
        }

        return written;
    }

    /// <summary>The expeditions the server holds, or none when the manager is not up.</summary>
    private static IReadOnlyList<Expedition> Expeditions()
    {
        return ExpeditionManager.Instance?.Expeditions?.ToList() ?? [];
    }

    /// <summary>
    /// Writes what a character gained, spent, caught or handed in since the last write. The boards
    /// themselves are rebuilt on their own tick; this only keeps the figures from being lost between two
    /// of them.
    /// </summary>
    public int SaveCharacter(MySqlConnection connection, MySqlTransaction transaction, Character character)
    {
        if (character == null)
            return 0;

        if (!character.RankGamePointTotals.HasPending && !character.RankRecords.HasPending)
            return 0;

        var now = DateTime.UtcNow;
        var holder = new RankScore
        {
            HolderKind = RankHolderKind.Character,
            HolderId = character.Id,
            AccountId = character.AccountId,
            WorldId = (byte)AppConfiguration.Instance.Id
        };

        var written = 0;

        var counters = new List<(RankDefinition Board, int Kind, int Method)>();
        var recordBoards = new List<RankDefinition>();
        foreach (var board in RankingGameData.Instance.Ranks)
        {
            if (RankingGameData.Instance.HolderKindOf(board) != RankHolderKind.Character)
                continue;

            if (RankingGameData.Instance.GamePointCounterOf(board) is { } counter)
                counters.Add((board, counter.Kind, counter.Method));
            else if (RankRecordRules.ForBoardKind(board.KindId) != null)
                recordBoards.Add(board);
        }

        // Every window keeps its own figures, so what is waiting is filed under each of them.
        if (character.RankGamePointTotals.HasPending && counters.Count > 0)
        {
            var pending = character.RankGamePointTotals.Pending;
            foreach (var window in counters
                         .Select(entry => RankingGameData.Instance.PeriodFor(entry.Board, now).StartUtc)
                         .Distinct())
            {
                store.AddGamePointTotals(connection, transaction, holder, window, pending, now);
            }

            character.RankGamePointTotals.Clear();
            written += pending.Count;
        }

        if (character.RankRecords.HasPending && recordBoards.Count > 0)
        {
            var records = character.RankRecords.Pending;
            foreach (var window in recordBoards
                         .Select(board => RankingGameData.Instance.PeriodFor(board, now).StartUtc)
                         .Distinct())
            {
                store.AddRecords(connection, transaction, holder, window, records, now);
            }

            character.RankRecords.Clear();
            written += records.Count;
        }

        return written;
    }

    /// <summary>
    /// Records a catch against the boards that rank what a character caught: how long the fish was, and
    /// what it weighed. The figures are the item's own, rolled when the fish was created.
    /// </summary>
    /// <remarks>
    /// The window reads both figures in thousandths — it draws the length as <c>length / 1000</c> under a
    /// centimetre heading and the weight as <c>weight / 1000</c> under a kilogram one — so a fish 249 cm
    /// long and 448 kg heavy is recorded as 249000 and 448000.
    /// </remarks>
    public static void RecordCatch(Character character, BigFish fish)
    {
        if (character == null || fish == null)
            return;

        var caughtAt = fish.CreateTime == default ? DateTime.UtcNow : fish.CreateTime;
        character.RankRecords.Add(RankRecordKind.FishLength, (long)Math.Round(fish.Length * 1000), caughtAt);
        character.RankRecords.Add(RankRecordKind.FishWeight, (long)Math.Round(fish.Weight * 1000), caughtAt);
    }

    /// <summary>
    /// Pays the boards whose window has ended since the last pass: the standings as they closed, the tier
    /// each place falls in, and what that tier owes â€” by mail, with the notice the window listens for.
    /// </summary>
    /// <remarks>
    /// A payout is recorded before it is granted, so a restart between the two pays a window once rather
    /// than twice; the standings it was computed from stay in the store either way. Only the window that
    /// closed most recently is paid: a server down for longer than one whole window leaves the older
    /// windows unpaid rather than handing out several cycles' rewards in one pass at boot.
    /// </remarks>
    public int PayEndedWindows(DateTime nowUtc)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var paid = PayEndedWindows(nowUtc, connection, transaction);
        transaction.Commit();
        return paid;
    }

    /// <summary>The same payout pass on a caller's connection and transaction.</summary>
    public int PayEndedWindows(DateTime nowUtc, MySqlConnection connection, MySqlTransaction transaction)
    {
        var paid = 0;
        foreach (var board in RankingGameData.Instance.Ranks)
        {
            // A board with no cycle never ends, so it never pays.
            if (board.ResetIntervalId == 0)
                continue;

            var previous = RankPayouts.Previous(board.ResetIntervalId, board.ResetDayOfWeekId,
                RankingGameData.Instance.PeriodFor(board, nowUtc));
            if (store.HasPayout(board.Id, previous.StartUtc))
                continue;

            var grants = RankPayouts.Plan(
                board,
                RankingGameData.Instance.TiersFor(board.Id),
                RankScoreboard.Place(store.ReadBoard(board.Id, previous.StartUtc, SCRankSnapshotPacket.MaxEntries), board.PermitTie));

            foreach (var grant in grants)
                Grant(grant);

            store.MarkPayout(board.Id, previous.StartUtc, nowUtc);
            if (grants.Count > 0)
                Logger.Info("Rankings: paid {0} place(s) of board {1} for the window that ended {2:u}",
                    grants.Count, board.Id, previous.EndUtc);

            paid += grants.Count;
        }

        return paid;
    }

    /// <summary>Hands one place its tier's item and currency, and tells a holder in world that it arrived.</summary>
    private void Grant(RankRewardGrant grant)
    {
        var holder = WorldManager.Instance.GetAllCharacters()?.FirstOrDefault(c => c.Id == grant.HolderId);

        if (grant.ItemId > 0 && grant.ItemCount > 0)
        {
            // The board is paid to whoever holds the place, so the holder is named from the character
            // table when they are not in world.
            var name = holder?.Name ?? NameManager.Instance.GetCharacterName((uint)grant.HolderId);
            if (!string.IsNullOrEmpty(name))
            {
                var mail = new BaseMail
                {
                    // The client has no separate mail type for a ranking payout in this build; the notice
                    // that the window reacts to is the reward-mail packet below.
                    MailType = MailType.Normal,
                    Title = "Ranking reward",
                    ReceiverName = name
                };
                mail.Header.SenderName = "Rankings";
                mail.Header.ReceiverId = (uint)grant.HolderId;
                mail.Header.Status = MailStatus.Unread;
                mail.Body.Text = $"Rank {grant.Position} on board {grant.RankId}.";
                mail.Body.RecvDate = DateTime.UtcNow;

                var item = ItemManager.Instance.Create(grant.ItemId, grant.ItemCount, (byte)grant.ItemGradeId, true);
                if (item != null)
                    mail.Body.Attachments.Add(item);

                mail.Send();
            }
        }

        if (grant.CurrencyId > 0 && grant.CurrencyAmount > 0 && holder != null)
            PayCurrency(holder, grant);

        if (holder?.Connection != null)
            holder.SendPacket(new SCRankRewardMailPacket((int)grant.RankId));
    }

    /// <summary>
    /// Pays a currency the game already has, as <c>enum_currencies</c> names it. A holder who is not in
    /// world is paid the item by mail but not the currency, which has nowhere to land without them loaded.
    /// </summary>
    private static void PayCurrency(Character holder, RankRewardGrant grant)
    {
        switch ((ContentCurrencyType)grant.CurrencyId)
        {
            case ContentCurrencyType.Gold:
            case ContentCurrencyType.GoldWithAaPoint:
                holder.AddMoney(SlotType.Inventory, grant.CurrencyAmount);
                break;
            case ContentCurrencyType.AaPoint:
                holder.AddAAPoint(SlotType.Inventory, grant.CurrencyAmount);
                break;
            case ContentCurrencyType.HonorPoint:
                holder.ChangeGamePoints(GamePointKind.Honor, grant.CurrencyAmount);
                break;
            case ContentCurrencyType.LivingPoint:
                holder.ChangeGamePoints(GamePointKind.Vocation, grant.CurrencyAmount);
                break;
            case ContentCurrencyType.ContributionPoint:
                // Contribution belongs to the expedition a character is in, and the boards that pay it are
                // the expedition boards, which have no values yet.
                if (!ExpeditionManager.Instance.TryChangeContributionPoints(holder, grant.CurrencyAmount, false))
                    Logger.Info("Rankings: contribution reward {0} not paid to {1} (no expedition)",
                        grant.CurrencyAmount, holder.Name);
                break;
            default:
                Logger.Info("Rankings: currency {0} not paid (board {1}, rank {2})",
                    grant.CurrencyId, grant.RankId, grant.Position);
                break;
        }
    }

    /// <summary>The lines of one board, best first, each with the place it holds.</summary>
    public List<RankPlace> ReadBoard(RankDefinition board, int limit)
    {
        var period = RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow);
        return ReadBoard(board, period.StartUtc, limit);
    }

    /// <summary>
    /// The lines one board held in one of its windows, best first, each with the place it held. A window
    /// that has closed keeps its lines, so a season that is over can still be read.
    /// </summary>
    public List<RankPlace> ReadBoard(RankDefinition board, DateTime periodStartUtc, int limit)
    {
        var scores = store.ReadBoard(board.Id, periodStartUtc, limit);
        return RankScoreboard.Place(scores, board.PermitTie);
    }

    /// <summary>
    /// One holder's own line on every board they stand on, which is what the window's personal answer
    /// carries. A board the holder has no value on is left out rather than sent as a zero.
    /// </summary>
    public List<RankingEntryLine> PersonalLines(Character character)
    {
        var lines = new List<RankingEntryLine>();
        if (character == null)
            return lines;

        var now = DateTime.UtcNow;
        foreach (var board in RankingGameData.Instance.Ranks)
        {
            var kind = RankingGameData.Instance.HolderKindOf(board);
            var holderId = HolderIdOf(board, kind, character);
            if (holderId == null)
                continue;

            var period = RankingGameData.Instance.PeriodFor(board, now);
            var score = store.ReadHolder(board.Id, period.StartUtc, kind, holderId.Value);
            if (score == null)
                continue;

            lines.Add(new RankingEntryLine(board.Id, new RankingEntry
            {
                V1 = score.Value,
                V2 = score.BareValue,
                Timestamp = new DateTimeOffset(score.UpdatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                WorldId = score.WorldId,
                Id = character.Id,
                AccountId = character.AccountId,
                Type = character.Id,
                PrivacyStatus = (byte)character.PrivacyStatus
            }));
        }

        return lines;
    }

    /// <summary>
    /// What a character is worth on a board, or null when the board does not measure them: below the
    /// board's floor, or wearing nothing the board counts. A board over equipped pieces carries the piece
    /// it measured, which is what its window names the line after.
    /// </summary>
    public static RankLine? CharacterScore(Character character, RankDefinition board, RankGate gate)
    {
        if (character == null)
            return null;

        if (board.DetailType == RankingGameData.GearScoreDetailType)
        {
            var parts = GearScoreCalculator.EvaluateParts(character);
            return RankingRules.GearScoreLine((long)Math.Truncate(parts.Total), (long)Math.Truncate(parts.Bare), gate.MinScore);
        }

        if (board.DetailType != RankingGameData.ItemDetailType)
            return null;

        var slots = RankingRules.ItemBoardSlots(board.Id);
        if (slots == null)
            return null;

        RankLine? best = null;
        foreach (var item in character.Inventory?.Equipment?.Items ?? [])
        {
            if (item is not EquipItem equip || equip.Template is not WeaponTemplate weapon)
                continue;

            var holdable = ItemManager.Instance.GetHoldable(weapon.HoldableTemplate?.Id ?? 0);
            if (holdable == null || !slots.Contains((byte)holdable.SlotTypeId))
                continue;

            if (!RankingRules.ItemCounts(equip, gate))
                continue;

            var gain = character.EquipSlotReinforces?.ItemLevelGain((byte)equip.Slot) ?? 0;
            var parts = GearScoreCalculator.EvaluateItemParts(equip, gain);
            var total = (long)Math.Truncate(parts.Total);
            var bare = (long)Math.Truncate(parts.Bare);
            if (best == null || total > best.Value.Value)
                best = RankingRules.GearScoreLine(total, bare, 0, RankingSubData.ForItem(equip.TemplateId));
        }

        return best;
    }

    /// <summary>Who holds a board's lines: the character, or the expedition they belong to.</summary>
    private static ulong? HolderIdOf(RankDefinition board, RankHolderKind kind, Character character)
    {
        // An expedition is a faction in this tree, so its id is the faction's.
        return kind == RankHolderKind.Expedition
            ? character.Expedition == null ? null : (ulong)character.Expedition.Id
            : character.Id;
    }
}
