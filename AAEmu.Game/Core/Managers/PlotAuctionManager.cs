using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.PlotAuctions;
using AAEmu.Game.Models.Tasks.PlotAuctions;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// The plot (housing land) auction behind CSPlotAuctionQueryInfo / PlaceBid / Exit and
/// SCPlotAuctionInfo / BidResponse / BidUpdate.
///
/// Money model: a bid moves the amount out of the bidder's wallet into a
/// <c>character_plot_auction_bids</c> escrow row (bid delta minus their standing bid, which is
/// what the client's own maxBid = cash + currentBid allows). The escrow row is the exactly-once
/// ledger: it is deleted immediately before a refund or prize is handed over and restored when
/// that hand-over fails — the same store-first / restore-on-failure idiom
/// <see cref="CraftOrderManager"/> posts and cancels with. Refunds go straight to the wallet
/// while the character is online and by letter when they are not (the craft-order expiry and
/// GF-E09's proceeds-by-mail convention), so a disconnect mid-auction changes nothing.
///
/// Settlement runs once per auction at <c>bid_end_time</c> (Initialize catches up on auctions
/// that ended while the process was down): the top <c>winner_count</c> bidders keep their escrow
/// — that is the winner paying — and receive the config's <c>rewards</c> by letter; everyone
/// else gets their held money back. A settled auction refuses a second settlement.
///
/// Retail currency note: the shipped row says payment is in AA Points and the money actually
/// moves through the Bill service (buySource=2). World has no Bill/cash balance here, so this
/// runs on the character wallet — the only money mover in the tree — and every deviation from
/// the Bill flow is logged rather than faked.
/// </summary>
public class PlotAuctionManager : Singleton<PlotAuctionManager>, ILoadable, IInitializable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// The client disables its exit button for the last 20 minutes of the bid window. Content has
    /// no row for that lock, so the server uses the same window.
    /// </summary>
    internal static readonly TimeSpan ExitLockout = TimeSpan.FromMinutes(20);

    /// <summary>
    /// How soon a settlement that still holds escrow rows is tried again. The due-time sweep skips
    /// auctions whose end has already passed, so a failed hand-over has to arm its own retry.
    /// </summary>
    private static readonly TimeSpan SettlementRetryInterval = TimeSpan.FromMinutes(1);

    private readonly object _lock = new();
    private readonly IWorldManager _worldManager;
    private readonly IItemManager _itemManager;

    private Dictionary<uint, PlotAuctionConfig> _configs = [];
    private Dictionary<uint, PlotAuction> _auctions = [];
    private Dictionary<uint, Dictionary<uint, PlotAuctionBid>> _bids = [];
    private IPlotAuctionStore _store = new InMemoryPlotAuctionStore();
    private IPlotAuctionWallet _wallet = new AccountCreditWallet();

    /// <summary>Tests stand in for account credits. Production uses <see cref="AccountCreditWallet"/>.</summary>
    internal void UseWallet(IPlotAuctionWallet wallet) =>
        _wallet = wallet ?? new AccountCreditWallet();
    private PlotAuctionSettleTask _settleTask;

    public PlotAuctionManager(IWorldManager worldManager, IItemManager itemManager)
    {
        _worldManager = worldManager;
        _itemManager = itemManager;
    }

    /// <summary>Content + persisted state, for tests and diagnostics.</summary>
    public IReadOnlyCollection<PlotAuctionConfig> Configs
    {
        get
        {
            lock (_lock)
                return _configs.Values.ToList();
        }
    }

    /// <summary>Live auction rows, for tests and diagnostics.</summary>
    public IReadOnlyCollection<PlotAuction> Auctions
    {
        get
        {
            lock (_lock)
                return _auctions.Values.ToList();
        }
    }

    /// <summary>Switches to the MySQL store, re-reads compact content and reloads persisted
    /// state. A store that cannot be read aborts startup (Load failures are not swallowed).</summary>
    public void Load()
    {
        lock (_lock)
        {
            _store = new MySqlPlotAuctionStore();
            using var connection = SQLite.CreateConnection();
            _configs = PlotAuctionContent.Load(connection);
            LoadFromStoreNoLock();
        }
    }

    /// <summary>Settles auctions whose window closed while World was down, then arms the sweep.</summary>
    public void Initialize()
    {
        SweepSettlement();
    }

    /// <summary>
    /// Answers the query: every config of the requested activity, with the viewer's own bid,
    /// rank and the auction's base price. The client's cache getters take only activityId
    /// (limited_auction_tab.lua lines 396-406), so the map carries the activity's full set.
    /// </summary>
    public void QueryInfo(Character character, uint activityId, uint plotId)
    {
        if (character == null)
            return;

        List<PlotAuctionBidInfoRow> rows;
        lock (_lock)
        {
            rows = [];
            foreach (var config in _configs.Values.Where(c => c.ActivityId == activityId))
                rows.Add(BuildInfoRowNoLock(config, character.Id));

            if (rows.Count == 0)
                Logger.Warn(
                    "Plot auction: query from {0} for activity {1} plot {2} matched no config",
                    character.Name, activityId, plotId);
            else if (!_configs.ContainsKey(plotId))
                Logger.Warn(
                    "Plot auction: query from {0} names plot {1}, which is no config of activity {2}; answering the activity's rows",
                    character.Name, plotId, activityId);
        }

        character.SendPacket(new SCPlotAuctionInfoPacket(activityId, rows));
    }

    /// <summary>
    /// Validates and takes a bid: escrow charge + row write in one step, refunds whoever the new
    /// standing kicked out of the money, then answers with the bid response and fans the new
    /// leading price out to everyone else. Returns the exact error code the response carries.
    /// </summary>
    public uint PlaceBid(Character character, uint activityId, uint auctionConfigId, long bidAmount)
    {
        if (character == null)
            return PlotAuctionErrorCodes.UnknownError;

        // The response's bidAmount field is s32 — never accept an amount the wire cannot carry.
        if (bidAmount <= 0 || bidAmount > int.MaxValue)
        {
            Respond(character, activityId, auctionConfigId, PlotAuctionErrorCodes.UnknownError, 0);
            return PlotAuctionErrorCodes.UnknownError;
        }

        uint code;
        long acceptedAmount = bidAmount;
        PlotAuction auction = null;
        lock (_lock)
        {
            code = PlaceBidNoLock(character, activityId, auctionConfigId, bidAmount, out auction);
            if (code != PlotAuctionErrorCodes.Success)
                acceptedAmount = 0;
        }

        Respond(character, activityId, auctionConfigId, code, acceptedAmount);
        if (code == PlotAuctionErrorCodes.Success)
        {
            BroadcastBidUpdate(character.Id, activityId, auctionConfigId, bidAmount);
            lock (_lock)
                ArmSettleSweepNoLock();
        }

        return code;
    }

    /// <summary>
    /// Pulls the character's standing bid back out of escrow (client-side this is the Exit
    /// button, disabled in the last 20 minutes). Returns the response's error code; 0 echoes a
    /// bidAmount of 0, which the client reads as "exit succeeded".
    /// </summary>
    public uint ExitBid(Character character, uint activityId, uint auctionConfigId)
    {
        if (character == null)
            return PlotAuctionErrorCodes.UnknownError;

        uint code;
        lock (_lock)
            code = ExitBidNoLock(character, activityId, auctionConfigId);

        Respond(character, activityId, auctionConfigId, code, 0);
        return code;
    }

    /// <summary>Settles every auction whose bid window has closed. Runs at Initialize (downtime
    /// catch-up), whenever a bid arms the sweep, and on the scheduled task.</summary>
    public void SweepSettlement()
    {
        List<uint> due;
        lock (_lock)
        {
            var now = ServerCalendar.UtcNow;
            due = _auctions.Values
                .Where(a => !a.Settled && _configs.TryGetValue(a.Id, out var cfg) &&
                            ServerCalendar.AsUtc(cfg.BidEndUtc) <= now)
                .Select(a => a.Id)
                .ToList();
        }

        foreach (var auctionId in due)
            TrySettle(auctionId);

        lock (_lock)
            ArmSettleSweepNoLock();
    }

    /// <summary>
    /// Settles one auction: winners (top <c>winner_count</c>) keep their held money — they have
    /// paid — and receive the configured rewards by letter; every other standing bid is refunded.
    /// Returns false when the auction is unknown, still open, already settled, or when at least
    /// one hand-over failed (the survivors are retried; a settled auction refuses a second run).
    /// </summary>
    public bool TrySettle(uint auctionConfigId)
    {
        lock (_lock)
        {
            if (!_auctions.TryGetValue(auctionConfigId, out var auction))
                return false;
            if (!_configs.TryGetValue(auctionConfigId, out var config))
            {
                Logger.Error("Plot auction: auction {0} has no config row; cannot settle", auctionConfigId);
                return false;
            }

            if (auction.Settled)
            {
                Logger.Warn("Plot auction: settlement of {0} refused — already settled", auctionConfigId);
                return false;
            }

            var now = ServerCalendar.UtcNow;
            if (ServerCalendar.AsUtc(config.BidEndUtc) > now)
            {
                Logger.Warn("Plot auction: settlement of {0} refused — bid window still open", auctionConfigId);
                return false;
            }

            var ranked = PlotAuctionRules.Ranked(BidsOfNoLock(auctionConfigId).Values);
            var winnerCount = (int)Math.Min(config.WinnerCount, (uint)ranked.Count);
            var survivors = 0;

            // Losers first: a failed refund must not stop a winner from receiving the prize —
            // each row settles independently and the auction only marks settled when empty.
            for (var i = winnerCount; i < ranked.Count; i++)
            {
                if (!TryConsumeEscrowNoLock(auction, config, ranked[i], prize: false))
                    survivors++;
            }

            for (var i = 0; i < winnerCount; i++)
            {
                if (!TryConsumeEscrowNoLock(auction, config, ranked[i], prize: true))
                    survivors++;
            }

            if (survivors > 0)
            {
                Logger.Error(
                    "Plot auction: settlement of {0} incomplete, {1} escrow row(s) still held; will retry",
                    auctionConfigId, survivors);
                return false;
            }

            auction.Settled = true;
            if (!_store.UpsertAuction(auction))
            {
                // The escrow rows are already consumed, so a reloaded World would re-run this
                // settlement against an empty bid set and reach the same place; the in-memory
                // flag still refuses a second attempt right now.
                auction.Settled = true;
                Logger.Error("Plot auction: could not persist settled flag for {0}", auctionConfigId);
            }

            Logger.Info("Plot auction: {0} settled ({1} winner(s))", auctionConfigId, winnerCount);
            return true;
        }
    }

    // ---------------------------------------------------------------- state seams

    internal void UseStore(IPlotAuctionStore store)
    {
        lock (_lock)
            _store = store ?? new InMemoryPlotAuctionStore();
    }

    internal void LoadContent(SqliteConnection connection)
    {
        lock (_lock)
            _configs = PlotAuctionContent.Load(connection);
    }

    /// <summary>Rebuilds the in-memory state from the store — exactly what a restart does.</summary>
    internal void LoadFromStore()
    {
        lock (_lock)
            LoadFromStoreNoLock();
    }

    /// <summary>Seeds an auction and its escrow rows as a boot-time reload would see them.</summary>
    internal void ImportStateForTest(PlotAuction auction, params PlotAuctionBid[] bids)
    {
        lock (_lock)
        {
            _auctions[auction.Id] = auction;
            _store.UpsertAuction(auction);
            var rows = BidsOfNoLock(auction.Id);
            foreach (var bid in bids)
            {
                rows[bid.CharacterId] = bid;
                _store.UpsertBid(bid);
            }
        }
    }

    internal PlotAuction AuctionFor(uint auctionConfigId)
    {
        lock (_lock)
            return _auctions.GetValueOrDefault(auctionConfigId);
    }

    internal IReadOnlyList<PlotAuctionBid> BidsFor(uint auctionConfigId)
    {
        lock (_lock)
            return PlotAuctionRules.Ranked(BidsOfNoLock(auctionConfigId).Values);
    }

    internal PlotAuctionConfig ConfigFor(uint auctionConfigId)
    {
        lock (_lock)
            return _configs.GetValueOrDefault(auctionConfigId);
    }

    // ---------------------------------------------------------------- core (call under _lock)

    private uint PlaceBidNoLock(
        Character character, uint activityId, uint auctionConfigId, long bidAmount, out PlotAuction auction)
    {
        auction = null;
        if (!_configs.TryGetValue(auctionConfigId, out var config) || config.ActivityId != activityId)
        {
            Logger.Warn("Plot auction: bid from {0} names unknown/mismatched auction {1} (activity {2})",
                character.Name, auctionConfigId, activityId);
            return PlotAuctionErrorCodes.NotInAuction;
        }

        if (!config.ActivityActive)
        {
            Logger.Warn("Plot auction: bid from {0} refused — activity {1} is off",
                character.Name, config.ActivityId);
            return PlotAuctionErrorCodes.NotInAuction;
        }

        var now = ServerCalendar.UtcNow;
        var phase = PlotAuctionRules.Phase(now, config);
        if (phase == PlotAuctionPhase.None)
            return now < ServerCalendar.AsUtc(config.BidStartUtc)
                ? PlotAuctionErrorCodes.NotInAuction
                : PlotAuctionErrorCodes.AuctionEnded;
        if (phase != PlotAuctionPhase.Bid)
            return PlotAuctionErrorCodes.NotInAuction; // preview: not bidable yet

        auction = _auctions.GetValueOrDefault(auctionConfigId);
        if (auction == null)
        {
            auction = new PlotAuction { Id = config.Id, ActivityId = config.ActivityId };
            if (!_store.UpsertAuction(auction))
            {
                Logger.Error("Plot auction: could not persist new auction {0}", config.Id);
                return PlotAuctionErrorCodes.UnknownError;
            }

            _auctions[config.Id] = auction;
            _bids[config.Id] = [];
        }

        var bids = BidsOfNoLock(auctionConfigId);
        bids.TryGetValue(character.Id, out var standing);
        var current = standing?.Amount ?? 0;
        if (bidAmount == current)
            return PlotAuctionErrorCodes.BidUnchanged;

        var basePrice = auction.BasePrice > 0 ? auction.BasePrice : config.StartPrice.Amount;
        var floor = PlotAuctionRules.NextBidFloor(basePrice, config.BidIncreasePct);
        if (bidAmount < floor)
        {
            Logger.Info("Plot auction: bid {0} from {1} below the floor {2} for auction {3}",
                bidAmount, character.Name, floor, config.Id);
            return PlotAuctionErrorCodes.BidTooLow;
        }

        // A standing bid is still held, so only the difference is charged — the same
        // cash + currentBid budget the client validates its input against.
        var delta = bidAmount - current;
        var previousBase = auction.BasePrice;
        auction.BasePrice = bidAmount;
        var replacement = new PlotAuctionBid
        {
            AuctionId = config.Id,
            CharacterId = character.Id,
            Amount = bidAmount,
            BidTimeUtc = now,
        };

        var row = auction;
        var written = _wallet.TryApply(character, -delta, (connection, transaction) =>
            _store.UpsertBid(replacement, connection, transaction) &&
            _store.UpsertAuction(row, connection, transaction));
        if (!written)
        {
            auction.BasePrice = previousBase;
            Logger.Info("Plot auction: {0} cannot escrow {1} credits for auction {2}",
                character.Name, delta, config.Id);
            return PlotAuctionErrorCodes.UnknownError;
        }

        bids[character.Id] = replacement;

        // Whoever the new standing kicked below winner_count is out of the money: refund now.
        // A failed refund keeps the row; settlement retries it, so the money cannot be lost
        // or handed over twice.
        var ranked = PlotAuctionRules.Ranked(bids.Values);
        for (var i = (int)Math.Min(config.WinnerCount, (uint)ranked.Count); i < ranked.Count; i++)
            TryConsumeEscrowNoLock(auction, config, ranked[i], prize: false);

        Logger.Info("Plot auction: {0} bid {1} on auction {2} (was {3})",
            character.Name, bidAmount, config.Id, current);
        return PlotAuctionErrorCodes.Success;
    }

    private uint ExitBidNoLock(Character character, uint activityId, uint auctionConfigId)
    {
        if (!_configs.TryGetValue(auctionConfigId, out var config) || config.ActivityId != activityId)
            return PlotAuctionErrorCodes.NotInAuction;
        if (!config.ActivityActive)
            return PlotAuctionErrorCodes.NotInAuction;

        var now = ServerCalendar.UtcNow;
        if (now < ServerCalendar.AsUtc(config.BidStartUtc))
            return PlotAuctionErrorCodes.NotInAuction;
        if (now >= ServerCalendar.AsUtc(config.BidEndUtc))
            return PlotAuctionErrorCodes.AuctionEnded;
        if (ServerCalendar.AsUtc(config.BidEndUtc) - now <= ExitLockout)
            return PlotAuctionErrorCodes.AuctionEnded;

        var auction = _auctions.GetValueOrDefault(auctionConfigId);
        var bids = auction == null ? null : BidsOfNoLock(auctionConfigId);
        if (auction == null || bids == null || !bids.TryGetValue(character.Id, out var standing))
        {
            // Nothing held: 500 is the client's own "not in auction" and its exit handler
            // treats it as already-succeeded, which makes exit idempotent.
            return PlotAuctionErrorCodes.NotInAuction;
        }

        if (!TryConsumeEscrowNoLock(auction, config, standing, prize: false))
            return PlotAuctionErrorCodes.UnknownError;

        // The leader just left: the floor falls back to the next standing bid (or the config
        // start price when nobody is left).
        var remaining = PlotAuctionRules.Ranked(bids.Values);
        auction.BasePrice = remaining.Count > 0 ? remaining[0].Amount : 0;
        if (!_store.UpsertAuction(auction))
            Logger.Error("Plot auction: could not persist the new base price for {0} after an exit",
                auctionConfigId);

        Logger.Info("Plot auction: {0} exited auction {1} and was refunded {2}",
            character.Name, auctionConfigId, standing.Amount);
        return PlotAuctionErrorCodes.Success;
    }

    /// <summary>
    /// Hands one escrow row over — the prize for a winner, the refund for anyone else — and
    /// deletes the row. The row goes first and comes back if the hand-over fails, so exactly one
    /// of {money still held, money delivered} can ever be true (craft-order cancel idiom).
    /// </summary>
    private bool TryConsumeEscrowNoLock(PlotAuction auction, PlotAuctionConfig config, PlotAuctionBid bid, bool prize)
    {
        if (!prize)
            return RefundEscrowNoLock(auction, bid);

        var bids = BidsOfNoLock(auction.Id);
        if (!_store.DeleteBid(bid.AuctionId, bid.CharacterId))
        {
            Logger.Error("Plot auction: could not remove escrow row {0}/{1}; hand-over skipped",
                bid.AuctionId, bid.CharacterId);
            return false;
        }

        bids.Remove(bid.CharacterId);

        if (!DeliverPrizeNoLock(config, bid, _worldManager.GetCharacterById(bid.CharacterId)))
        {
            bids[bid.CharacterId] = bid;
            if (!_store.UpsertBid(bid))
                Logger.Error(
                    "Plot auction: CRITICAL — escrow row {0}/{1} could not be restored after a failed hand-over; {2} copper unaccounted",
                    bid.AuctionId, bid.CharacterId, bid.Amount);
            return false;
        }

        return true;
    }

    /// <summary>Credits a bid whose row is already gone (a prize that had nothing to deliver).</summary>
    private bool CreditBid(PlotAuctionBid bid)
    {
        var online = _worldManager.GetCharacterById(bid.CharacterId);
        if (online != null)
            return _wallet.TryApply(online, bid.Amount, (_, _) => true);

        var accountId = NameManager.Instance.GetCharacterAccount(bid.CharacterId);
        return accountId != 0 && _wallet.TryCreditAccount(accountId, bid.Amount, (_, _) => true);
    }

    /// <summary>
    /// Returns the held credits and deletes the bid row in one step. Online or not, the refund
    /// is account credits — a copper letter would pay a different currency than the bid.
    /// </summary>
    private bool RefundEscrowNoLock(PlotAuction auction, PlotAuctionBid bid)
    {
        var bids = BidsOfNoLock(auction.Id);
        var online = _worldManager.GetCharacterById(bid.CharacterId);
        bool credited;
        if (online != null)
        {
            credited = _wallet.TryApply(online, bid.Amount, (connection, transaction) =>
                _store.DeleteBid(bid.AuctionId, bid.CharacterId, connection, transaction));
        }
        else
        {
            var accountId = NameManager.Instance.GetCharacterAccount(bid.CharacterId);
            if (accountId == 0)
            {
                Logger.Error(
                    "Plot auction: no account resolves for offline {0} on auction {1}; {2} credits stay escrowed",
                    bid.CharacterId, bid.AuctionId, bid.Amount);
                return false;
            }

            credited = _wallet.TryCreditAccount(accountId, bid.Amount, (connection, transaction) =>
                _store.DeleteBid(bid.AuctionId, bid.CharacterId, connection, transaction));
        }

        if (!credited)
        {
            Logger.Error("Plot auction: could not return {0} credits for bid {1}/{2}",
                bid.Amount, bid.AuctionId, bid.CharacterId);
            return false;
        }

        bids.Remove(bid.CharacterId);
        return true;
    }

    private bool DeliverPrizeNoLock(PlotAuctionConfig config, PlotAuctionBid bid, Character online)
    {
        var name = NameManager.Instance.GetCharacterName(bid.CharacterId)
                   ?? online?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            Logger.Error("Plot auction: no recipient resolves for winner {0} of auction {1}",
                bid.CharacterId, config.Id);
            return false;
        }

        var prizes = new List<Item>();
        foreach (var (rewardType, itemId, count) in config.Rewards)
        {
            if (rewardType != 1)
            {
                // Only the item reward (type 1) is pinned — the client checks exactly that
                // (limited_auction_tab.lua line 1623). Anything else is content we refuse to guess.
                Logger.Error(
                    "Plot auction: config {0} reward type {1} (item {2}) is not a kind this server can deliver",
                    config.Id, rewardType, itemId);
                foreach (var created in prizes)
                    _itemManager.ReleaseId(created.Id);
                return false;
            }

            if (count == 0 || count > int.MaxValue)
            {
                Logger.Error("Plot auction: config {0} reward count {1} cannot be delivered",
                    config.Id, count);
                foreach (var created in prizes)
                    _itemManager.ReleaseId(created.Id);
                return false;
            }

            var item = _itemManager.Create(itemId, (int)count, 0);
            if (item == null)
            {
                // Loud-missing: the row's item template is not in the shipped items table, so
                // the winner's escrow stays held (and the error repeats) instead of the prize
                // silently vanishing with the money.
                Logger.Error("Plot auction: config {0} reward item {1} does not exist; winner keeps escrow",
                    config.Id, itemId);
                foreach (var created in prizes)
                    _itemManager.ReleaseId(created.Id);
                return false;
            }

            prizes.Add(item);
        }

        if (prizes.Count == 0)
        {
            // Paying for nothing would be worse than refusing: hand the bid back instead.
            Logger.Error("Plot auction: config {0} has no deliverable rewards; refunding winner {1}",
                config.Id, bid.CharacterId);
            return CreditBid(bid);
        }

        var mail = MailForPlotAuction.ForPrize(bid.CharacterId, name, config.Name, bid.Amount, prizes);
        if (MailManager.Instance.SendBatch([mail]))
        {
            Logger.Info("Plot auction: winner {0} received the prize of auction {1} for {2}",
                name, config.Id, bid.Amount);
            return true;
        }

        foreach (var created in prizes)
            _itemManager.ReleaseId(created.Id);
        Logger.Error("Plot auction: prize letter for {0} on auction {1} was refused",
            bid.CharacterId, config.Id);
        return false;
    }

    // ---------------------------------------------------------------- helpers

    private Dictionary<uint, PlotAuctionBid> BidsOfNoLock(uint auctionConfigId)
    {
        if (!_bids.TryGetValue(auctionConfigId, out var rows))
        {
            rows = [];
            _bids[auctionConfigId] = rows;
        }

        return rows;
    }

    private void LoadFromStoreNoLock()
    {
        _auctions = _store.LoadAuctions().ToDictionary(a => a.Id);
        _bids = [];
        foreach (var bid in _store.LoadBids())
            BidsOfNoLock(bid.AuctionId)[bid.CharacterId] = bid;
    }

    private PlotAuctionBidInfoRow BuildInfoRowNoLock(PlotAuctionConfig config, uint viewerId)
    {
        var ranked = PlotAuctionRules.Ranked(BidsOfNoLock(config.Id).Values);
        _auctions.TryGetValue(config.Id, out var auction);

        var myRank = 0u;
        var myAmount = 0L;
        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].CharacterId != viewerId)
                continue;
            myRank = (uint)(i + 1);
            myAmount = ranked[i].Amount;
            break;
        }

        return new PlotAuctionBidInfoRow
        {
            PlotId = config.Id,
            MyBidAmount = myAmount,
            MyRanking = myRank,
            CurrentWinningBid = ranked.Count > 0 ? ranked[0].Amount : 0,
            TotalBidders = (uint)ranked.Count,
            BasePrice = auction?.BasePrice ?? 0,
        };
    }

    private static void Respond(Character character, uint activityId, uint auctionConfigId, uint code, long bidAmount) =>
        character.SendPacket(
            new SCPlotAuctionBidResponsePacket(activityId, auctionConfigId, code, (uint)bidAmount));

    /// <summary>SCPlotAuctionBidUpdate goes to everyone else online — the client reads it as
    /// "someone else bid higher" (limited_auction_tab.lua line 601).</summary>
    private void BroadcastBidUpdate(uint excludeCharacterId, uint activityId, uint auctionConfigId, long newBidAmount)
    {
        foreach (var connection in GameConnectionTable.Instance.GetConnections())
        {
            var other = connection.ActiveChar;
            if (other == null || other.Id == excludeCharacterId)
                continue;
            other.SendPacket(
                new SCPlotAuctionBidUpdatePacket(activityId, auctionConfigId, (uint)newBidAmount));
        }
    }

    /// <summary>Arms one shot at the next still-open bid_end (call under _lock). Auctions that
    /// are already due are settled by the sweep that precedes this call.</summary>
    private void ArmSettleSweepNoLock()
    {
        var now = ServerCalendar.UtcNow;
        DateTime? earliest = null;
        foreach (var (auctionId, auction) in _auctions)
        {
            if (auction.Settled || !_configs.TryGetValue(auctionId, out var config))
                continue;
            var due = ServerCalendar.AsUtc(config.BidEndUtc);
            if (due <= now)
                continue;
            if (earliest == null || due < earliest)
                earliest = due;
        }

        if (earliest == null)
        {
            var overdue = _auctions.Any(pair =>
                !pair.Value.Settled &&
                _configs.TryGetValue(pair.Key, out var config) &&
                ServerCalendar.AsUtc(config.BidEndUtc) <= now);
            if (!overdue)
                return;
            earliest = now + SettlementRetryInterval;
        }

        var delay = earliest.Value - now;
        if (delay <= TimeSpan.Zero)
            return;

        if (_settleTask != null && SingletonContainer.ServiceProvider != null)
            TaskManager.Instance.Cancel(_settleTask);
        _settleTask = new PlotAuctionSettleTask();
        TaskManager.Instance.Schedule(_settleTask, delay);
    }
}
